using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using OpenAI_API;
namespace Stevebot
{

    public class Chat
    {
        static Engine td2 = new("text-davinci-002");
        public static OpenAIAPI OpenAI = new OpenAI_API.OpenAIAPI(new APIAuthentication(File.ReadAllText("Constants/openaitoken").Trim('\n')), engine: td2);
        public class ChatMessage
        {
            public ulong Sender { get; }
            public string Message { get; }
            public DateTime Time { get; }

            public ChatMessage(ulong sender, string message)
            {
                Sender = sender;
                Message = message;
                Time = DateTime.Now;
            }
            public ChatMessage(ulong sender, string message, DateTime time)
            {
                Sender = sender;
                Message = message;
                Time = time;
            }
        }

        public class ChatUser
        {
            public ulong Id { get; }
            public DateTime LastMsg { get; set; }
            public bool Left { get; set; } = false;

            public ChatUser(ulong id)
            {
                Id = id;
                LastMsg = DateTime.Now;
            }
        }


        public static List<Chat> Chats = new List<Chat>();

        public List<ChatUser> users{ get; } = new List<ChatUser>();
        public ulong channel_id { get; }
        public List<ChatMessage> messageHistory { get; set; }

        bool just_listening = false;
        int messagesUntilJoin = 0;

        DateTime lastTimeSent = DateTime.MinValue;
        int secondDelay = 0;
        const int MEMORY_LENGTH = 15;

        private string[] prompts = {
                                        "This is a chat log between an all-knowing but kind and humorous Artificial Intelligence, [BOT], and a human, [USER]. The current date is [DATE].",
                                        "This is a chat log between some users in a university chat room for York University, in Toronto Canada. Occasionally, an Artificial Intelligence known as [BOT] chimes in with his knowledge banks or just to have fun. The current date is [DATE]."
                                   };

        public Chat(ulong user, ulong channel, string botFirstMsg)
        {
            users.Add(new ChatUser(user));
            channel_id = channel;
            messageHistory = new List<ChatMessage>();
            messageHistory.Add(new ChatMessage(ForkBot.Constants.Users.FORKBOT,botFirstMsg));
            Chats.Add(this);
        }

        public Chat(ulong user, ulong channel, bool listening = false)
        {
            users.Add(new ChatUser(user));
            channel_id = channel;
            messageHistory = new List<ChatMessage>();
            if (listening) {
                just_listening = true;
                messagesUntilJoin = ForkBot.Bot.rdm.Next(3, MEMORY_LENGTH+1);
            }
            Chats.Add(this);
        }

        public void Join(IUser user)
        {
            users.Add(new ChatUser(user.Id));

            messageHistory.Add(new ChatMessage(0, $"{user.Username} has entered the chat."));
        }

        // returns true if there are no users remaining
        public bool Leave(IUser user)
        {
            bool found = false;
            users.Where(x => x.Id == user.Id).First().Left = true;

            messageHistory.Add(new ChatMessage(0, $"{user.Username} has left the chat."));
                        
            if (users.Where(x => x.Left = false).Count() == 0)
            {
                Chats.Remove(this);
                return true;
            }
            return false;
        }


        public async Task<string> GetNextMessageAsync(IMessage message)
        {

            messageHistory.Add(new ChatMessage(message.Author.Id,message.Content.Replace(";talk","").Trim(' ')));

            bool botMentioned = message.Content.ToLower().Contains("forkbot") || message.MentionedUserIds.Contains(ForkBot.Constants.Users.FORKBOT);
            bool timePassed = (DateTime.Now - lastTimeSent) > new TimeSpan(0, 0, secondDelay);

            if (!botMentioned && !timePassed)
                return "";

            int max = (users.Where(x => !x.Left).Count() - 1) * 7;
            secondDelay = ForkBot.Bot.rdm.Next(0, max); // random amount of seconds from 0 to (7 * (#ofusers - 1))
            Console.WriteLine($"[DEBUG] second delay from 0 to {max}");
            lastTimeSent = DateTime.Now;
            
            
            string botName = ForkBot.Bot.client.CurrentUser.Username;
            if (just_listening)
            {
                if (--messagesUntilJoin > 0) return "";
                else if (messagesUntilJoin == 0) {
                    var bbGen = await ForkBot.Bot.client.GetChannelAsync(ForkBot.Constants.Channels.GENERAL) as IGuildChannel;
                    var gUser = await bbGen.GetUserAsync(ForkBot.Bot.client.CurrentUser.Id);
                    await gUser.ModifyAsync(x => x.Nickname = gUser.DisplayName.Replace(ForkBot.Constants.Emotes.EAR.Name,""));
                }
            }

            using (message.Channel.EnterTypingState())
            {
                string fullMsg;
                if (just_listening) fullMsg = prompts[1];
                else fullMsg = prompts[0];
                //string dnl = "\n\n"; // double newline
                string dnl = "  ";
                fullMsg = fullMsg.Replace("[BOT]", botName).Replace("[USER]", (await ForkBot.Bot.client.GetUserAsync(users[0].Id)).Username).Replace("[DATE]", DateTime.Now.ToString("MMMM d, hh:mmtt")) + dnl;

                int start = messageHistory.Count() - (MEMORY_LENGTH - 1);
                
                for (int i = start >= 0 ? start : 0; i < messageHistory.Count(); i++)
                {
                    fullMsg += $"[{messageHistory[i].Time.ToShortTimeString()}] ";
                    if (messageHistory[i].Sender != 0)
                    {
                        if (messageHistory[i].Sender == ForkBot.Constants.Users.FORKBOT) fullMsg += botName + ": \"";
                        else
                        {
                            var user = await ForkBot.Bot.client.GetUserAsync(messageHistory[i].Sender);
                            fullMsg += $"{user.Username}: \"";
                        }
                        fullMsg += messageHistory[i].Message + "\"" + dnl;
                    }
                    else fullMsg += messageHistory[i].Message + dnl;
                }

                fullMsg += $"[{DateTime.Now.ToShortTimeString()}] " + botName + ": \"";
                //fullMsg = fullMsg.Replace("\n", "[NEWLINE]");
		        Console.Write(fullMsg);
                var response = await OpenAI.Completions.CreateCompletionAsync(fullMsg, temperature: 0.85, max_tokens: 128, stopSequences: "\"");
                messageHistory.Add(new ChatMessage(ForkBot.Constants.Users.FORKBOT, response.ToString()));
                System.Threading.Thread.Sleep(response.ToString().Length * 75);
                return await ReplaceNameWithPingAsync(response.ToString());
            }
        }

        async Task<string> ReplaceNameWithPingAsync(string msg)
        {
            foreach(var u in users)
            {
                var user = await ForkBot.Bot.client.GetUserAsync(u.Id);
                if (msg.Contains(user.Username))
                    msg = msg.Replace(user.Username, $"<@{user.Id}>");
            }

            return msg;
        }
    }
}
