using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using OpenAI.GPT3.ObjectModels.RequestModels;
using OpenAI.GPT3.ObjectModels;
using OpenAI.GPT3;
using ForkBot;
using System.Text.RegularExpressions;
using System.Data.SQLite;

namespace Stevebot
{

    public class Chat
    {
        #region subclasses
        public class Message
        {
            public string Role { get; }
            public ulong Sender { get; }
            public string Text { get; }
            public DateTime Time { get; }

            public Message(string role, ulong sender, string message)
            {
                Sender = sender;
                Text = message;
                Time = DateTime.Now;
                Role = role;
            }
            public Message(string role, ulong sender, string message, DateTime time)
            {
                Sender = sender;
                Text = message;
                Time = time;
                Role = role;
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

            public int GetTokenCount()
            {
                var user = User.Get(Id);
                int usedWords = user.GetData<int>("GPTWordsUsed");
                return (int)(usedWords * 1.4);
            }
        }
        #endregion

        // Publics
        public static OpenAI.GPT3.Managers.OpenAIService OpenAI = new OpenAI.GPT3.Managers.OpenAIService(new OpenAiOptions()
        {
            ApiKey = File.ReadAllText("Constants/openaitoken").Trim('\n')
        });

        public static List<Chat> Chats = new List<Chat>();
        public List<ChatUser> users { get; } = new List<ChatUser>();
        public ulong channel_id { get; }
        public List<Message> messageHistory { get; set; }

        

        // Privates
        bool just_listening = false;
        int messagesUntilJoin = 0;
        DateTime lastTimeSent = DateTime.MinValue;
        int secondDelay = 0;

        // Constants
        const int MEMORY_LENGTH = 15;
        ulong BOT_ID = Constants.Users.FORKBOT;
        public const string MIN_BOT_NAME = "fork";
        public const float MONEY_AVAILABLE = 7.5f;
        public const int MAX_USER_TOKENS = (int)(((MONEY_AVAILABLE / 20f) / 0.02f) * 250f);

        private string[] prompts = {
                                        "This is a chat log between an all-knowing but kind and humorous Artificial Intelligence, [BOT], and a human. The current date is [DATE].",
                                        "This is a chat log between some users in Toronto, Canada. Occasionally, an Artificial Intelligence known as [BOT] chimes in with his knowledge banks or just to have fun. The current date is [DATE].",
                                        "This is a chat log between some users in Toronto, Canada. The current date is [DATE]." // in case we want to act less robotly
                                   };

        public Chat(ulong user, ulong channel, string botFirstMsg)
        {
            users.Add(new ChatUser(user));
            channel_id = channel;
            messageHistory = new List<Message>();
            messageHistory.Add(new Message("assistant", BOT_ID, botFirstMsg));
            Chats.Add(this);
        }

        public Chat(ulong user, ulong channel, bool listening = false)
        {
            users.Add(new ChatUser(user));
            channel_id = channel;
            messageHistory = new List<Message>();
            if (listening)
            {
                just_listening = true;
                messagesUntilJoin = Bot.rdm.Next(3, MEMORY_LENGTH + 1);
            }
            Chats.Add(this);
        }

        public ChatUser GetUser(ulong id) { return users.FirstOrDefault(x => x.Id == id); }

        public void Join(IUser user)
        {
            if (users.Where(x => x.Id == user.Id).Count() == 0)
            {
                users.Add(new ChatUser(user.Id));
                Console.WriteLine($"[DEBUG] {user.Username} has entered the chat.");
                messageHistory.Add(new Message("system", 0, $"{user.Username} has entered the chat."));
            }

        }


        public void Leave(IUser user)
        {
            bool found = false;
            users.Where(x => x.Id == user.Id).First().Left = true;

            Console.WriteLine($"[DEBUG] {user.Username} has left the chat. {users.Where(x => x.Left == false).Count()}/{users.Count()}");
            messageHistory.Add(new Message("system", 0, $"{user.Username} has left the chat."));

            if (users.Where(x => x.Left == false).Count() == 0)
            {
                (Bot.client.GetChannel(channel_id) as ITextChannel).SendMessageAsync(Constants.Emotes.WAVE.ToString());
                Chats.Remove(this);
            }
        }

        public async Task Update()
        {
            foreach (var user in users)
            {
                if (DateTime.Now - user.LastMsg > TimeSpan.FromMinutes(5)) Leave(await Bot.client.GetUserAsync(user.Id));
            }
        }

        public async Task<List<ChatMessage>> BuildMessageList()
        {
            List<ChatMessage> list = new List<ChatMessage>();
            foreach (var msg in messageHistory)
            {
                string content = "";
                if (msg.Sender == 0)
                    content = msg.Text;
                else
                    content = $"[{msg.Time.ToShortTimeString()}] {(await Bot.client.GetUserAsync(msg.Sender)).Username}: {msg.Text}";
                var chatmsg = new ChatMessage(msg.Role, content);
                list.Add(chatmsg);
            }
            return list;
        }

        public async Task<string> GetNextMessageAsync(IMessage message)
        {
            var chatUser = GetUser(message.Author.Id);
            var user = new User(message.Author.Id);

            if (chatUser.GetTokenCount() > MAX_USER_TOKENS)
                return "";
            else
                user.AddData("GPTWordsUsed", Regex.Matches(message.Content, "\\w+|[,.!?]").Count() * 2);

            chatUser.LastMsg = DateTime.Now;
            messageHistory.Add(new Message("user", message.Author.Id, message.Content.Replace(Constants.Values.COMMAND_PREFIX + "talk", "").Trim(' ')));

            bool botMentioned = message.Content.ToLower().Contains(MIN_BOT_NAME) || message.MentionedUserIds.Contains(BOT_ID);
            bool timePassed = (DateTime.Now - lastTimeSent) > new TimeSpan(0, 0, secondDelay);

            if (!botMentioned && !timePassed)
                return "";

            int activeUsers = users.Where(x => !x.Left).Count();
            int ignoreChance = (activeUsers - 1) * 7;
            bool ignore = Bot.rdm.Next(0, 100) < (ignoreChance < 100 ? ignoreChance : 100);

            if (!botMentioned && ignore)
                return "";

            int max = (activeUsers - 1) * 3;
            secondDelay = Bot.rdm.Next(0, max); // random amount of seconds from 0 to (7 * (#ofusers - 1))
            Console.WriteLine($"[DEBUG] second delay from 0 to {max} is {secondDelay}");
            lastTimeSent = DateTime.Now;


            string botName = Bot.client.CurrentUser.Username;
            if (just_listening)
            {
                if (--messagesUntilJoin > 0) return "";
                else if (messagesUntilJoin == 0)
                {
                    var Gen = await Bot.client.GetChannelAsync(Constants.Channels.GENERAL) as IGuildChannel;
                    var gUser = await Gen.GetUserAsync(Bot.client.CurrentUser.Id);
                    await gUser.ModifyAsync(x => x.Nickname = gUser.DisplayName.Replace(Constants.Emotes.EAR.Name, ""));
                }
            }

            using (message.Channel.EnterTypingState())
            {
                string intro;
                if (just_listening) intro = prompts[2];
                else intro = prompts[0];
                string dnl = "\n\n"; // double newline

                var memory = DBFunctions.GetProperty("chat_memory").ToString();

                var intro_msg = new ChatMessage("system", intro.Replace("[BOT]","ForkBot").Replace("[DATE]",DateTime.Now.ToShortDateString()) + '\n' + memory);

                var msgs = await BuildMessageList();



                msgs.Insert(0, intro_msg);

                var chat_request = new ChatCompletionCreateRequest()
                {
                    PresencePenalty = 0.5f,
                    Temperature = 0.85f,
                    Messages= msgs
                };


                //var completion = await OpenAI.Completions.CreateCompletion(request, Models.ChatGpt3_5Turbo);
                var completion = await OpenAI.ChatCompletion.CreateCompletion(chat_request, Models.ChatGpt3_5Turbo);
                if (completion.Successful)
                {
                    string response = completion.Choices.First().Message.Content;

                    //System.Threading.Thread.Sleep(response.ToString().Length * 75); disabled for forkbot
                    response = Regex.Replace(response, "^\\[([()a-zA-Z0-9: ]+)\\]( [a-zA-Z0-9]+)?:? ?", "");
                    messageHistory.Add(new Message("assistant", BOT_ID, response));
                    response = await ReplaceNameWithPingAsync(response);
                    return response;
                }
                else
                {
                    Chats.Remove(this);

                    if (completion.Error.Type == "insufficient_quota")
                        return "Sorry!\nWe've used up all of our OpenAI API Funds.\n\nIf you'd like to donate more, you can at https://www.paypal.me/Brady0423. 100% will go to our usage limit.\nDonating $5+ will also give you an item that bypasses the monthly per-user usage limit.";
                    else
                    {
                        Console.WriteLine("[ERROR] " + completion.Error.Type + "\n" + completion.Error.Message);
                        return "Sorry! There was an error with OpenAI. If this was unexpected, let Brady#0010 know.";
                    }
                }
            }
        }

        async Task<string> ReplaceNameWithPingAsync(string msg)
        {
            foreach (var u in users)
            {
                var user = await Bot.client.GetUserAsync(u.Id);
                if (msg.Contains(user.Username))
                    msg = msg.Replace(user.Username, $"<@{user.Id}>");
            }

            return msg;
        }

        public static async void ChatTimerCallBack(object sender, System.Timers.ElapsedEventArgs e)
        {
            for (int i = Chat.Chats.Count() - 1; i >= 0; i--)
            {
                await Chat.Chats[i].Update();
            }
        }

        public static double GetAllTokensUsed()
        {
            using (var con = new SQLiteConnection(Constants.Values.DB_CONNECTION_STRING))
            {
                con.Open();
                var stm = $"SELECT SUM(GPTWordsUsed) FROM USERS;";
                using (var cmd = new SQLiteCommand(stm, con))
                {
                    return Convert.ToInt32(cmd.ExecuteScalar()) * 1.4;
                }
            }
        }
    }
}
