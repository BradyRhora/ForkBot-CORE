using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Net;
using System.IO;
using HtmlAgilityPack;
using System.Data.SQLite;

namespace ForkBot
{
    public class Bot
    {
        static void Main(string[] args) => new Bot().Run().GetAwaiter().GetResult();

        public static Random rdm = new();

        public static DiscordSocketClient client;
        public static CommandService commands;
        public static List<User> users = new();
        private const bool LOG = true;
        public async Task Run()
        {
            try
            {                
                DiscordSocketConfig config = new() { GatewayIntents = GatewayIntents.All,MessageCacheSize = 1000 };
                Console.WriteLine("Welcome. Initializing ForkBot CORE...");
                client = new DiscordSocketClient(config);
                if (LOG) client.Log += LogAsync;
                Console.WriteLine("Client Initialized.");
                commands = new CommandService();
                Console.WriteLine("Command Service Initialized.");
                await InstallCommands();
                Console.WriteLine("Commands Installed, logging in.");
                if (!Directory.Exists("Constants"))
                {
                    Directory.CreateDirectory("Constants");
                    Console.WriteLine($"Created Constants folder in {Directory.GetCurrentDirectory()}");
                }
                if (!File.Exists("Constants/bottoken"))
                {
                    File.WriteAllText("Constants/bottoken", "");
                    Console.WriteLine($"Created bottoken file in {Path.GetDirectoryName("Constants")}, you will need to put the token in this file.");
                }
                
                await client.LoginAsync(TokenType.Bot, File.ReadAllText("Constants/bottoken"));
                Console.WriteLine("Successfully logged in!");
                await client.StartAsync();
                Var.DebugCode = rdm.Next(999, 9999) + 1;
                Var.IDEnd = rdm.Next(10);
                Console.WriteLine($"ForkBot CORE successfully intialized with debug code [{Var.DebugCode}]");
                Var.startTime = Var.CurrentDate();

                int strikeCount = (Var.CurrentDate() - Constants.Dates.REBIRTH).Days;
                await client.SetGameAsync(strikeCount + " days since REBIRTH", type: ActivityType.Watching);
                Timers.MinuteTimer = new Timer(Timers.MinuteUpdate, null, 1000 * 30, 1000 * 60);
                await Task.Delay(-1);
            }
            catch (Exception e)
            {
                Console.WriteLine("\n==========================================================================");
                Console.WriteLine("                                  ERROR                        ");
                Console.WriteLine("==========================================================================\n");
                Console.WriteLine($"Error occured in {e.Source}");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.InnerException);
                Console.WriteLine(e.StackTrace);
                Console.Read();
            }
        }
        public async Task LogAsync(LogMessage msg)
        {
            Console.WriteLine($"[General/{msg.Severity}] {msg}");
        }

        public async Task InstallCommands()
        {
            client.MessageReceived += HandleMessage;
            client.ReactionAdded += HandleReact;
            await commands.AddModulesAsync(Assembly.GetEntryAssembly(), services: null);
        }

       
    List<ulong> newUsers = new();

        public async Task HandleMessage(SocketMessage messageParam)
        {
            SocketUserMessage message = messageParam as SocketUserMessage;

            if (message == null) return;
            else if (message.Author.Id == client.CurrentUser.Id) return; //doesn't allow the bot to respond to itself
            else if (!Var.DebugMode && message.Channel.Id == Constants.Channels.DEBUG) return; //hides debug channel messages if not in debug mode
            else if (Var.DebugMode && message.Author.Id != Constants.Users.BRADY && Var.DebugUsers.Where(x => x.Id == message.Author.Id).Count() <= 0) return; //only allows brady or allowed users to use commands if in debug mode
            else if (Var.blockedUsers.Where(x => x.Id == message.Author.Id).Count() > 0) return; //prevents "blocked" users from using the bot

            bool isDM = await Functions.isDM(message);
            if (isDM)
            {
                if (!message.Content.StartsWith(";"))   
                    Console.WriteLine(message.Author.Username + " says:\n" + message.Content);
                else if (Var.LockDM) { 
                    Console.WriteLine(message.Author.Username + " [" + message.Author.Id + "] attempted to use a command in DM's:\n'" + message.Content + "'");
                    return;
                }
            }
            if (message == null) return;
            
            var user = User.Get(message.Author);

            //present stuff
            string presentNum = Convert.ToString(Var.presentNum);
            if (Var.presentWaiting && message.Content == presentNum)
            {
                Var.presentWaiting = false;
                var presents = DBFunctions.GetItemIDList();
                int presID;
                do
                {
                    var presIndex = rdm.Next(presents.Length);
                    presID = presents[presIndex];
                } while (!DBFunctions.ItemIsPresentable(presID));
                Var.present = DBFunctions.GetItemName(presID);
                Var.rPresent = Var.present;
                var presentName = Var.present;
                var pMessage = DBFunctions.GetItemDescription(presID);
                var msg = $"{message.Author.Username}! You got...\n{Functions.GetPrefix(presentName)} {Func.ToTitleCase(presentName.Replace('_', ' '))}! {DBFunctions.GetItemEmote(presID)} {pMessage}";
                user.GiveItem(Var.present);

                if (Var.replaceable)
                {
                    msg += $"\nDon't like this gift? Press {Var.presentNum} again to replace it once!";
                    Var.replacing = true;
                    Var.presentReplacer = message.Author;
                }
                await message.Channel.SendMessageAsync(msg);
            }
            else if (Var.replaceable && Var.replacing && message.Content == presentNum && message.Author == Var.presentReplacer)
            {
                if (user.HasItem(Var.present))
                {
                    user.RemoveItem(Var.present);
                    await message.Channel.SendMessageAsync($":convenience_store: {DBFunctions.GetItemEmote(Var.present)} :runner: \nA **new** present appears! :gift: Press {Var.presentNum} to open it!");
                    Var.presentWaiting = true;
                    Var.replacing = false;
                    Var.replaceable = false;
                }
                else
                {
                    await message.Channel.SendMessageAsync("You no longer have the present, so you cannot replace it!");
                    Var.replacing = false;
                    Var.replaceable = false;
                }
            }


            //detect and execute commands
            int argPos = 0;
            if (message.HasCharPrefix(';', ref argPos))
            {
                // new user prevention
                var userCreationDate = message.Author.CreatedAt;
                var existenceTime = DateTime.UtcNow.Subtract(userCreationDate.DateTime);
                var fewDays = new TimeSpan(3, 0, 0, 0);
                if (existenceTime < fewDays && !message.Content.Contains("verify"))
                {
                    if (!newUsers.Contains(message.Author.Id))
                    {
                        newUsers.Add(message.Author.Id);
                        await message.Author.SendMessageAsync("Hi there! Welcome to Discord. In order to avoid bot abuse, your account must be older than a few days to use the bot.\n" +
                            "If you don't understand, just message <@108312797162541056> about it.\nThanks!");
                    }
                    return;
                }

                var context = new CommandContext(client, message);
                var result = await commands.ExecuteAsync(context, argPos, services: null);

                if (!result.IsSuccess)
                {
                    if (result.Error != CommandError.UnknownCommand)
                    {
                        Console.WriteLine(result.ErrorReason);
                        var emb = new InfoEmbed("ERROR:", result.ErrorReason).Build();
                        await message.Channel.SendMessageAsync("", embed: emb);
                    }
                    else
                    {
                        int itemID = DBFunctions.GetItemID(message.Content.Trim(';'));
                        if (itemID != -1 && user.HasItem(itemID))
                        {
                            var tip = DBFunctions.GetItemTip(itemID);
                            if (tip != null)
                            await message.Channel.SendMessageAsync(tip);
                            else
                            await message.Channel.SendMessageAsync("Nothing happens... *Use `;suggest [suggestion]` if you have an idea for this item!*");
                        }
                    }
                }
                else
                {
                    //if chance of lootbox
                    if (user.GetData<DateTime>("LastLootboxAttempt") <= Var.CurrentDate() - new TimeSpan(1, 0, 0))
                    {
                        //10% chance at lootbox
                        if ((rdm.Next(100) + 1) < 10)
                        {
                            await context.Channel.SendMessageAsync(":package: `A lootbox appears in your inventory!`");
                            user.GiveItem("package");
                        }
			            user.SetData("LastLootboxAttempt", Var.CurrentDate()); // set last msg time
                    }

                }
            }
            else if (Stevebot.Chat.Chats.Select(x => x.channel_id == message.Channel.Id).Count() != 0)
            {
                Stevebot.Chat chat = Stevebot.Chat.Chats.Where(x => x.channel_id == message.Channel.Id).First();

                bool exited = false;
                if (chat.users.Where(x => x.Id == message.Author.Id).Count() == 0)
                    chat.Join(message.Author);

                if (chat.users.Where(x => x.Id == message.Author.Id).First().Left == false)
                {
                    var response = await chat.GetNextMessageAsync(message);
                    await message.Channel.SendMessageAsync(response);

                    string[] partingTerms = { "goodbye", "seeya", "cya" };
                    if (partingTerms.Where(x => message.Content.Contains(x)).Count() > 0)
                        if (chat.Leave(message.Author))
                            await message.Channel.SendMessageAsync(Constants.Emotes.WAVE.Name);
                }
            }
            else if (lastChatCheck < DateTime.Now - new TimeSpan(0, 10, 0))
            {
                ulong[] allowedChannels = {Constants.Channels.GENERAL,Constants.Channels.COMMANDS,Constants.Channels.DEV };
                if (allowedChannels.Contains(message.Channel.Id))
                {
                    lastChatCheck = DateTime.Now;
                    int chance = rdm.Next(1000);
                    Console.WriteLine(chance);
                    if (chance <= LISTEN_CHANCE * 10)
                    {
                        var gUser = (message.Channel as SocketGuildChannel).GetUser(client.CurrentUser.Id);
                        await gUser.ModifyAsync(x => x.Nickname = gUser.DisplayName + Constants.Emotes.EAR.Name);
                        Console.WriteLine("I want to join the chat..");
                        Stevebot.Chat chat = new Stevebot.Chat(message.Author.Id, message.Channel.Id, true);
                    }
                }
            }

        }
        const int LISTEN_CHANCE = 5; //%
        public static DateTime lastChatCheck = new DateTime(0);


        public async Task HandleReact(Cacheable<IUserMessage, ulong> cache, Cacheable<IMessageChannel, ulong> channel, SocketReaction react)
        {
            if (cache.Value == null) return;
            if ((react.UserId != client.CurrentUser.Id))
            {
                string tag = null;
                Discord.Rest.RestUserMessage message = null;
                foreach (IMessage msg in Var.awaitingHelp)
                {
                    if (msg.Id == cache.Value.Id)
                    {
                        if (react.Emote.Name == Constants.Emotes.HAMMER.Name) tag = "[MOD]";
                        else if (react.Emote.Name == Constants.Emotes.DIE.Name) tag = "[FUN]";
                        else if (react.Emote.Name == Constants.Emotes.QUESTION.Name) tag = "[OTHER]";
                        else if (react.Emote.Name == Constants.Emotes.BRADY.Name) tag = "[BRADY]";
                        message = msg as Discord.Rest.RestUserMessage;
                        Var.awaitingHelp.Remove(msg);
                        break;
                    }
                }

                if (tag != null)
                {
                    JEmbed emb = new();

                    emb.Author.Name = "ForkBot Commands";
                    emb.ColorStripe = Constants.Colours.DEFAULT_COLOUR;

                    foreach (CommandInfo c in commands.Commands)
                    {
                        string cTag = null;
                        if (c.Summary != null)
                        {
                            if (c.Summary.StartsWith("["))
                            {
                                int index;
                                index = c.Summary.IndexOf(']') + 1;
                                cTag = c.Summary.Substring(0, index);
                            }
                            else cTag = "[OTHER]";

                            if (cTag != null && cTag == tag)
                            {
                                emb.Fields.Add(new JEmbedField(x =>
                                {
                                    string header = ';' + c.Name;
                                    foreach (string alias in c.Aliases) if (alias != c.Name) header += " (;" + alias + ") ";
                                    foreach (Discord.Commands.ParameterInfo parameter in c.Parameters) header += " [" + parameter.Name + "]";
                                    x.Header = header;
                                    x.Text = c.Summary.Replace(tag + " ", "");
                                }));
                            }
                        }
                        
                    }
                    await message.ModifyAsync(x => x.Embed = emb.Build());
                    await message.RemoveAllReactionsAsync();
                }
            }
        }
    }
}

