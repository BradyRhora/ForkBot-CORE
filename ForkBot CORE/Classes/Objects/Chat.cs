using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using OpenAI.ObjectModels.RequestModels;
using OpenAI.ObjectModels;
using OpenAI;
using ForkBot;
using System.Text.RegularExpressions;
using System.Data.SQLite;
using System.Net;

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

            public bool UseTokensIfAvailable(int tokenCount)
            {
                if (GetTokenCount() + tokenCount > MAX_USER_TOKENS)
                    return false;

                var user = new User(Id);
                user.AddData("GPTWordsUsed", tokenCount);
                return true;
            }

            public User GetUser()
            {
                return User.Get(Id);
            }
        }

        public class BotResponse
        {
            public string Text { get; }
            public byte[] Img { get; }
            public bool HasImage { get; }

            public BotResponse(string text, byte[] img = null)
            {
                Text = text;
                Img = img;
                HasImage = img != null;
            }
        }
        #endregion

        // Publics
        public static OpenAI.Managers.OpenAIService OpenAI = new OpenAI.Managers.OpenAIService(new OpenAiOptions()
        {
            ApiKey = File.ReadAllText("Constants/openaitoken").Trim('\n')
        });

        public static List<Chat> Chats = new List<Chat>();
        public List<ChatUser> Users { get; } = new List<ChatUser>();
        public ulong ChannelID { get; }
        public List<Message> MessageHistory { get; set; }
        public string Topic { get; set; }

        

        // Privates
        bool just_listening = false;
        int messagesUntilJoin = 0;
        DateTime lastTimeSent = DateTime.MinValue;
        int secondDelay = 0;

        // Constants
        const int MEMORY_LENGTH = 20;
        ulong BOT_ID = Constants.Users.FORKBOT;
        public const string MIN_BOT_NAME = "fork";
        public const float MONEY_AVAILABLE = 30f;
        public const int MAX_USER_TOKENS = (int)(((MONEY_AVAILABLE / 20f) / 0.02f) * 250f);

        private string[] prompts = {
                                        "This is a chat log between an all-knowing but kind and humorous Artificial Intelligence, [BOT], and a human. The current date is [DATE].",
                                        "This is a chat log between some users in Toronto, Canada. Occasionally, an Artificial Intelligence known as [BOT] chimes in with his knowledge banks or just to have fun. The current date is [DATE].",
                                        "This is a chat log between some users in Toronto, Canada. The current date is [DATE]. Messages shouldn't be too lengthy unless necessary.", // in case we want to act less robotly
                                        "Hi ChatGPT. You're chatting with multiple users in a Discord Server who like to be witty and joke around, so make sure to have fun with them! This chat has DALLE integration, and f at any point, ChatGPT decides an image is an appropriate respose, he must include the keyword '[IMAGE]' (with square brackets) followed by a description of the image he wants to send as a DALLE prompt which cannot be read by the user."
                                   };

        
        public Chat(ulong user, ulong channel, string botFirstMsg = "", bool listening = false, string topic = "")
        {
            Users.Add(new ChatUser(user));
            MessageHistory = new List<Message>();
            ChannelID = channel;
            Topic = topic;

            if (botFirstMsg != "")
                MessageHistory.Add(new Message("assistant", BOT_ID, botFirstMsg));

            if (listening)
            {
                just_listening = true;
                messagesUntilJoin = Math.Min(15, Bot.rdm.Next(3, MEMORY_LENGTH + 1));
            }

            Chats.Add(this);
        }

        public ChatUser GetUser(ulong id) { return Users.FirstOrDefault(x => x.Id == id); }

        public void Join(IUser user)
        {
            if (Users.Where(x => x.Id == user.Id).Count() == 0)
            {
                Users.Add(new ChatUser(user.Id));
                Console.WriteLine($"[DEBUG] {user.Username} has entered the chat.");
                MessageHistory.Add(new Message("system", 0, $"{user.Username} has entered the chat."));
            }

        }


        public void Leave(IUser user)
        {
            bool found = false;
            Users.Where(x => x.Id == user.Id).First().Left = true;

            Console.WriteLine($"[DEBUG] {user.Username} has left the chat. {Users.Where(x => x.Left == false).Count()}/{Users.Count()}");
            MessageHistory.Add(new Message("system", 0, $"{user.Username} has left the chat."));

            if (Users.Where(x => x.Left == false).Count() == 0)
            {
                (Bot.client.GetChannel(ChannelID) as ITextChannel).SendMessageAsync(Constants.Emotes.WAVE.ToString());
                Chats.Remove(this);
            }
        }

        public async Task Update()
        {
            if (MessageHistory.Count() == 0) return;

            var lastMsg = MessageHistory.Last();
            if (lastMsg.Sender != Constants.Users.FORKBOT && DateTime.Now - lastMsg.Time > TimeSpan.FromMinutes(2))
            {
                var msg = await GetNextMessageAsync();
                await (Bot.client.GetChannel(ChannelID) as ITextChannel).SendMessageAsync(msg.Text);
            }

            foreach (var user in Users)
            {
                if (!user.Left)
                    if (DateTime.Now - user.LastMsg > TimeSpan.FromMinutes(5)) Leave(await Bot.client.GetUserAsync(user.Id));
            }
        }

        public async Task<List<ChatMessage>> BuildMessageList()
        {
            List<ChatMessage> list = new List<ChatMessage>();
            int useLength = Math.Min(MessageHistory.Count(), MEMORY_LENGTH);
            foreach (var msg in MessageHistory.GetRange(MessageHistory.Count() - useLength, useLength))
            {
                string content = "";
                if (msg.Sender == 0)
                    content = msg.Text;
                else
                    content = $"{(await Bot.client.GetUserAsync(msg.Sender)).Username}: {msg.Text}";
                var chatmsg = new ChatMessage(msg.Role, content);
                list.Add(chatmsg);
            }
            return list;
        }

        int GetTokenWorth(string msg)
        {
            return Regex.Matches(msg, "\\w+|[,.!?]").Count() * 2;
        }

        void AddToHistory(ChatUser user, IMessage msg)
        {
            user.LastMsg = DateTime.Now;
            MessageHistory.Add(new Message("user", msg.Author.Id, msg.Content.Replace(Constants.Values.COMMAND_PREFIX + "talk", "").Trim(' ')));
        }



        //TODO: Break this up into smaller functions
        public async Task<BotResponse> GetNextMessageAsync(IMessage? message = null)
        {
            if (message != null)
            {
                var chatUser = GetUser(message.Author.Id);
                var user = chatUser.GetUser();

                // Ensure user has tokens available

                if (!chatUser.UseTokensIfAvailable(GetTokenWorth(message.Content)))
                    return new BotResponse("");

                if (chatUser.GetTokenCount() > MAX_USER_TOKENS && !user.HasItem("keyboard"))
                    return new BotResponse("");
                else
                    user.AddData("GPTWordsUsed", Regex.Matches(message.Content, "\\w+|[,.!?]").Count() * 2);
                
                
                // Add to history
                chatUser.LastMsg = DateTime.Now;
                MessageHistory.Add(new Message("user", message.Author.Id, message.Content.Replace(Constants.Values.COMMAND_PREFIX + "talk", "").Trim(' ')));


                AddToHistory(chatUser, message);

                // Check if bot has been called by name and if respond delay has passed
                bool botMentioned = message.Content.ToLower().Contains(MIN_BOT_NAME) || message.MentionedUserIds.Contains(BOT_ID);
                bool timePassed = (DateTime.Now - lastTimeSent) > new TimeSpan(0, 0, secondDelay);

                if (!botMentioned && !timePassed)
                    return new BotResponse("");

                // Check if bot should ignore user
                int activeUsers = Users.Where(x => !x.Left).Count();
                double sensitivity = .445;
                int ignoreChance = (int)((100 / ((activeUsers + 1) * Math.Log10(sensitivity))) + 100);
                Console.WriteLine($"[DEBUG] with {activeUsers} users, ignore chance is {ignoreChance}%");
                bool ignore = Bot.rdm.Next(0, 100) < (ignoreChance < 100 ? ignoreChance : 100);

                if (!botMentioned && ignore)
                    return new BotResponse("");

                // Calculate delay based on number of users
                int max = ((activeUsers) * 4) + 4;
                secondDelay = Bot.rdm.Next(4, max);
                Console.WriteLine($"[DEBUG] second delay from 0 to {max} is {secondDelay}");
            }

            lastTimeSent = DateTime.Now;

            // Check if bot should join chat or continue just listening
            string botName = Bot.client.CurrentUser.Username;
            if (just_listening)
            {
                Console.WriteLine($"[DEBUG] listening for {--messagesUntilJoin} more messages");
                if (messagesUntilJoin > 0) return new BotResponse("");
                else if (messagesUntilJoin == 0)
                {
                    var Gen = await Bot.client.GetChannelAsync(Constants.Channels.GENERAL) as IGuildChannel;
                    var gUser = await Gen.GetUserAsync(Bot.client.CurrentUser.Id);
                    await gUser.ModifyAsync(x => x.Nickname = gUser.DisplayName.Replace(Constants.Emotes.EAR.Name, ""));
                }
            }

            var channel = (ITextChannel)(await Bot.client.GetChannelAsync(ChannelID));

            using (channel.EnterTypingState())
            {
                string intro = prompts[3]; // oh yea baby now we're playing with DAN
                intro += "\n" + Topic;
                /*
                if (just_listening) intro = prompts[2];
                else intro = prompts[2]; // ik this doesnt make a diff rn
                */
                var memory = DBFunctions.GetProperty("chat_memory").ToString();
                var intro_msg = new ChatMessage("system", intro.Replace("[BOT]","ForkBot").Replace("[DATE]",DateTime.Now.ToShortDateString()) + '\n' + memory);
                var msgs = await BuildMessageList();

                msgs.Insert(0, intro_msg);

                var chat_request = new ChatCompletionCreateRequest()
                {
                    PresencePenalty = 0.25f,
                    Temperature = 0.85f,
                    Messages= msgs
                };


                //var completion = await OpenAI.Completions.CreateCompletion(request, Models.ChatGpt3_5Turbo);
                var completion = await OpenAI.ChatCompletion.CreateCompletion(chat_request, Models.ChatGpt3_5Turbo);
                if (completion.Successful)
                {
                    string response = completion.Choices.First().Message.Content;
                    Console.WriteLine("[DEBUG] Response: " + response);

                    //System.Threading.Thread.Sleep(response.ToString().Length * 75); disabled for forkbot
                    string edit_response = Regex.Replace(response, "^([a-zA-Z0-9 ]*): ?", "");

                    if (edit_response.ToLower().Contains("[image]") || response.ToLower().Contains("[image]"))
                    {
                        Console.WriteLine("[DEBUG] Image detected");
                        var key_loc = edit_response.ToLower().IndexOf("[image]");
                        key_loc = (key_loc == -1) ? 0 : (key_loc) + "[image]".Length;

                        Console.WriteLine("[DEBUG] key found at: " + key_loc);
                        string msg = edit_response.Substring(0, key_loc - "[image]".Length).Trim();
                        string prompt = edit_response.Substring(key_loc).Trim();

                        if (prompt == "")
                        {
                            prompt = msg;
                            msg = "";
                        }

                        Console.WriteLine("[DEBUG/msg] " + msg);
                        Console.WriteLine("[DEBUG/prompt] " + prompt);


                        var img = await OpenAI.CreateImage(new ImageCreateRequest(prompt));
                        MessageHistory.Add(new Message("assistant", BOT_ID, response));

                        if (img.Successful)
                        {
                            Console.Write($"[DEBUG] Image generated with url: [{img.Results.First().Url}], attempting to download...");
                            //Download image from URL
                            var webClient = new WebClient();
                            var data = webClient.DownloadData(img.Results.First().Url);
                            Console.WriteLine(" And send...");
                            return new BotResponse(msg, data);
                        }
                        else
                        {
                            Console.WriteLine("[DEBUG] Image failed to generate: " + img.Error);
                            msgs.Add(new ChatMessage("system", "The image failed to generate due to:\n" + img.Error));
                            var new_response = await OpenAI.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest()
                            {
                                PresencePenalty = 0.5f,
                                Temperature = 0.85f,
                                Messages = msgs
                            });
                            edit_response = Regex.Replace(new_response.Choices.First().Message.Content, "^([a-zA-Z0-9 ]*): ?", "");
                        }
                    }

                    MessageHistory.Add(new Message("assistant", BOT_ID, edit_response));
                    edit_response = await ReplaceNameWithPingAsync(edit_response);
                    return new BotResponse(edit_response);
                    
                }
                else
                {
                    Chats.Remove(this);

                    if (completion.Error.Type == "insufficient_quota")
                        return new BotResponse("Sorry!\nWe've used up all of our OpenAI API Funds.\n\nIf you'd like to donate more, you can at https://www.paypal.me/Brady0423. 100% will go to our usage limit.\nDonating $5+ will also give you an item that bypasses the monthly per-user usage limit.");
                    else if (completion.Error.Type == "server_error")
                        return new BotResponse("lol sry openAI is overloaded with requests lmao\nlemme try again");
                    else
                    {
                        Console.WriteLine("[ERROR] " + completion.Error.Type + "\n" + completion.Error.Message);
                        return new BotResponse("Sorry! There was an error with OpenAI. If this was unexpected, let Brady#0010 know.");
                    }
                }
            }
        }

        async Task<string> ReplaceNameWithPingAsync(string msg)
        {
            foreach (var u in Users)
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
