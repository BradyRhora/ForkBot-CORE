using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using HtmlAgilityPack;
using System.Drawing;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Globalization;
using System.Data.SQLite;
using System.Data;
using YorkU;
using OpenAI.GPT3.ObjectModels.RequestModels;

namespace ForkBot
{
    public class Commands : ModuleBase
    {
        readonly Random rdm = new();
        readonly Exception NotBradyException = new("This command can only be used by Brady.");

        #region Useful

        [Command("help"), Summary("Displays commands and descriptions.")]
        public async Task Help()
        {
            JEmbed emb = new ();
            emb.Author.Name = "ForkBot Commands";
            emb.ThumbnailUrl = Context.User.AvatarId;
            if (Context.Guild != null) emb.ColorStripe = Functions.GetColor(Context.User);
            else emb.ColorStripe = Constants.Colours.DEFAULT_COLOUR;

            emb.Description = "Select the emote that corresponds to the commands you want to see.";

            emb.Fields.Add(new JEmbedField(x =>
            {
                x.Text = ":hammer:";
                x.Header = "MOD COMMANDS";
                x.Inline = true;
            }));

            emb.Fields.Add(new JEmbedField(x =>
            {
                x.Text = ":game_die:";
                x.Header = "FUN COMMANDS";
                x.Inline = true;
            }));

            emb.Fields.Add(new JEmbedField(x =>
            {
                x.Text = ":question:";
                x.Header = "OTHER COMMANDS";
                x.Inline = true;
            }));

            if (Context.User.Id == Constants.Users.BRADY)
            {
                emb.Fields.Add(new JEmbedField(x =>
                {
                    x.Text = Constants.Emotes.BRADY.ToString();
                    x.Header = "BRADY COMMANDS";
                    x.Inline = true;
                }));
            }


            var msg = await Context.Channel.SendMessageAsync("", embed: emb.Build());

            await msg.AddReactionAsync(Constants.Emotes.HAMMER);
            await msg.AddReactionAsync(Constants.Emotes.DIE);
            await msg.AddReactionAsync(Constants.Emotes.QUESTION);
            if (Context.User.Id == Constants.Users.BRADY) await msg.AddReactionAsync(Constants.Emotes.BRADY);
            Var.awaitingHelp.Add(msg);
        }

        [Command("help"), Summary("Displays information about a specific command.")]
        public async Task Help(string command)
        {
            var cmd = Bot.commands.Commands.Where(x => x.Name.ToLower() == command.ToLower()).FirstOrDefault();

            if (cmd != null)
            {
                JEmbed emb = new()
                {
                    ThumbnailUrl = Context.User.AvatarId
                };
                if (Context.Guild != null) emb.ColorStripe = Functions.GetColor(Context.User);
                else emb.ColorStripe = Constants.Colours.DEFAULT_COLOUR;

                string comTitle = ";" + cmd.Name;

                foreach (string alias in cmd.Aliases) if (alias != cmd.Name) comTitle += " (;" + alias + ") ";
                foreach (ParameterInfo parameter in cmd.Parameters) comTitle += " [" + parameter.Name + "]";

                emb.Author.Name = comTitle;

                emb.Description = Regex.Replace(cmd.Summary,"(\\[[A-Z]*\\])","");

                var msg = await Context.Channel.SendMessageAsync("", embed: emb.Build());
            }
            else await ReplyAsync($"Command ;{command} not found.");

        }


        HtmlWeb web = new();
        Dictionary<string,string> GetProfStats(string name, string[] inputStats)
        {
            string link = "https://www.ratemyprofessors.com/search.jsp?query=" + name.Replace(" ", "%20");
            var page = web.Load(link).DocumentNode;
            var str = page.SelectSingleNode("/html[1]/body[1]/script[1]").InnerHtml;
            List<string> statNames = inputStats.ToList();

            bool adding = false;
            string addingStr = "";
            int first = -1, second = -1;
            Dictionary<string,string> stats = new ();
            for(int i = 0; i < str.Length - 6 && statNames.Count > 0; i++)
            {
                if (!adding)
                {
                    foreach (string s in statNames)
                    {
                        if (str[i] == s[0])
                        {
                            if (str.Substring(i-1, s.Length+2) == '"'+s+'"')
                            {
                                adding = true;
                                addingStr = s;
                            }
                        }
                    }
                }
                else
                {
                    if (str[i] == ':') first = i;
                    else if (str[i] == ',')
                    {
                        second = i-1;
                        string stat = str.Substring(first + 1, second - first).Trim('"');
                        stats.Add(addingStr, stat);
                        statNames.Remove(addingStr);
                        adding = false;
                    }
                }
            }

            return stats;
        }

        [Command("professor"), Alias(new string[] { "prof", "rmp" }), Summary("Check out a professors rating from RateMyProfessors.com!")]
        public async Task Professor([Remainder] string name)
        {
            var stats = GetProfStats(name, new string[] {"avgRating","wouldTakeAgainPercent","avgDifficulty","department","firstName","lastName","name","numRatings" });

            string profName = stats["firstName"] + " " + stats["lastName"];
            string school = stats["name"];
            JEmbed emb = new()
            {
                Title = profName + " - " + school,
                Description = "Department of " + stats["department"]
            };
            emb.Fields.Add(new JEmbedField(x =>
            {
                x.Header = "Rating:";
                x.Text = stats["avgRating"] + $"({stats["numRatings"]} ratings)";
                x.Inline = true;
            }));

            emb.Fields.Add(new JEmbedField(x =>
            {
                x.Header = "Difficulty:";
                x.Text = stats["avgDifficulty"];
                x.Inline = true;
            }));

            emb.Fields.Add(new JEmbedField(x =>
            {
                x.Header = "Would take again?:";
                x.Text = stats["wouldTakeAgainPercent"];
                x.Inline = true;
            }));

            emb.Footer = new JEmbedFooter("Info scraped from www.ratemyprofessors.com");

            emb.ColorStripe = Constants.Colours.YORK_RED;
            await Context.Channel.SendMessageAsync("", embed: emb.Build());
        }

        [Command("course"), Summary("Shows details for a course using inputted course code.")]
        public async Task Course([Remainder] string code = "")
        {
            Course course = null;
            string term = "";
            try
            {
                if (code == "")
                {
                    await ReplyAsync($"To use this command, use the format:\n" +
                        "`;course [subject][level]`\n" +
                        "For example, if you want the course information for MATH1190, simply type: `;course math1190`.\n" +
                        "You can also specify the term of the course you want, either FW or SU (for fall/winter and summer respectively). For example,\n" +
                        "`;course eecs4404 fw`\n" +
                        $"will give you the fall/winter course for EECS4404. If you don't specify, it will use the current term. *(Current term is:* **{Var.term}** *)*");
                    return;
                }

                code = code.ToLower();
                //bool force = false;
                if (code.Split(' ').Contains("fw")) term = "fw";
                else if (code.Split(' ').Contains("su")) term = "su";
                //else if (code.Split(' ').Contains("force")) force = true;

                code = code.Replace(" fw", "").Replace(" su", "").Replace(" force", "");


                //formats course code correctly
                if (Regex.IsMatch(code, "([A-z]{2,4} *[0-9]{4})"))
                {
                    var splits = Regex.Split(code, "(\\d+|\\D+)").Where(x => x != "").ToArray();
                    code = splits[0].Trim() + " " + splits[1].Trim();
                }

                if (term == "") term = YorkU.Course.GetCurrentTerm();
                course = new Course(code,term:term);


                JEmbed emb = new()
                {
                    Title = course.Title,
                    TitleUrl = course.ScheduleLink,
                    Description = course.Description,
                    ColorStripe = Constants.Colours.YORK_RED
                };

                foreach (CourseDay day in course.GetSchedule().Days)
                {
                    emb.Fields.Add(new JEmbedField(x =>
                    {
                        x.Header = $"**__{day.Term} - {day.Section}__**";
                        x.Text = $"{day.Professor.Replace("Section Director:","**Section Director:**")}\n";
                        if (day.HasLabs) x.Text += "This section has labs, click the title to see times and catalog numbers.\n";
                        else x.Text += $"**Catalog Number**: {day.CAT}\n";
                        foreach (var dayTime in day.DayTimes)
                        {
                            if (dayTime.Value == DateTime.MinValue) x.Text += "\nOnline";
                            else x.Text += $"\n{dayTime.Key} at {dayTime.Value.ToShortTimeString()}";
                        }
                    }));
                    
                }

                emb.Footer.Text = "Term: " + course.Term;


                await Context.Channel.SendMessageAsync("", embed: emb.Build());
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message + "\n" + e.StackTrace);
                if (course != null && course.CourseNotFound) await ReplyAsync($"The specified course was not found. If you know this course exists, try `;course {code} force`. This may be very slow, so only do it once. It will then be added to the courselist and should work normally.");
                else await ReplyAsync($"There was an error loading the course page. (Possibly not available this term: **{term.ToUpper()}**)\nTry appending a different term to the end of the command (e.g. `;course {code} fw`)");
            }

        }

        [Command("courselist"), Summary("Displays all courses for the inputted subject.")]
        public async Task CourseList(string subject, int page = 1)
        {
            if (page < 1)
            {
                await ReplyAsync("Please input a valid page number.");
                return;
            }

            string[] courses = File.ReadAllLines("Files/courselist.txt");
            string list = "";
            foreach (string course in courses)
            {
                var data = course.Split('/');
                if (data.Length > 1)
                    if (data[1].StartsWith(subject.ToUpper())) list += course + "\n";
            }

            if (list == "")
            {
                await ReplyAsync($"No courses found with subject: {subject}");
                return;
            }

            string[] msgs = Functions.SplitMessage(list);


            if (page > msgs.Length) page = msgs.Length - 1;

            JEmbed courseEmb = new();
            courseEmb.Author.Name = $"{subject.ToUpper()} Course List";
            courseEmb.Author.IconUrl = Constants.Images.ForkBot;
            courseEmb.ColorStripe = Constants.Colours.YORK_RED;

            courseEmb.Description = msgs[page - 1];

            courseEmb.Footer.Text = $"Page {page}/{msgs.Length} (Use ';courselist {subject.ToUpper()} #' and replace the number with a page number!)";

            await ReplyAsync("", embed: courseEmb.Build());
        }

        [Command("suggest"), Summary("Suggest something for ForkBot, whether it's an item, an item's function, a new command, or anything else! People who abuse this will be blocked from using it.")]
        public async Task Suggest([Remainder] string suggestion)
        {
            var brady = await Bot.client.GetUserAsync(Constants.Users.BRADY);
            var sBlocked = User.Get(Context.User);
            await brady.SendMessageAsync("", embed: new InfoEmbed("SUGGESTION FROM: " + Context.User.Username, suggestion).Build());
            await Context.Channel.SendMessageAsync("Suggestion submitted.");
        }

        [Command("updates"), Summary("See the most recent update log.")]
        public async Task Updates()
        {
		await Context.Channel.SendMessageAsync("```\nFORKBOT CORE 1.0 - Ladies and Gentlemen, he's back.\n- Migrated from .NET Framework to .NET CORE 6.0 for Ubuntu Server compatibility\n- Fixed many bugs relating to migration\n- Reset user database (stats, items, etc.) for a fresh start and to make it more welcoming for new users.\nMore to come!```");
        }

        [Command("stats"), Summary("See stats regarding Forkbot."), Alias("uptime")]
        public async Task Stats()
        {
            var guilds = Bot.client.Guilds;
            int guildCount = guilds.Count;
            IGuildUser[] users = new IGuildUser[0];
            foreach (IGuild g in guilds)
            {
                users = (await g.GetUsersAsync()).Union(users).ToArray();
            }
            var userCount = users.Length;
            var uptime = Var.CurrentDate() - Var.startTime;

            JEmbed emb = new()
            {
                Title = "ForkBot Stats",
                Description = $"ForkBot is developed by Brady#0010 for use in the York University Discord server.\nIt has many uses, such as professor lookup, course lookup, and many fun commands with coins, items, and more!",
                ColorStripe = Constants.Colours.YORK_RED
            };
            emb.Fields.Add(new JEmbedField(x =>
            {
                x.Header = "Users";
                x.Text = $"Serving {userCount} unique users in {guildCount} guilds.";
            }));
            emb.Fields.Add(new JEmbedField(x =>
            {
                x.Header = "Uptime";
                x.Text = $"{uptime.Days} days, {uptime.Hours} hours, {uptime.Minutes} minutes, {uptime.Seconds} seconds.";
            }));
            emb.ThumbnailUrl = Constants.Images.ForkBot;
            await ReplyAsync("", embed: emb.Build());
        }

        [Command("remind"), Summary("Sets a message to remind you of in the specified amount of time."), Alias(new string[] { "reminder", "rem" })]
        public async Task Remind([Remainder] string parameters = "")
        {
            if (parameters == "")
            {
                await ReplyAsync("This command reminds you of the message you choose, in the amount of time that you specify. You may have max five reminders at a time.\n" +
                                 "Seperate the message you want to be reminded of and the amount of time with the keyword `in`. If you have multiple `in`'s the last one will be used.\n" +
                                 "eg: `;remind math1019 midterm in 6 days and 3 hours`\nYou can use any combination of days, hours, minutes. " +
                                 "Seperate each using either a comma or the word `and`.\n" +
                                 "Use `;reminderlist` to view your reminders and `;deletereminder [#]` with the reminder number to delete it.");
            }
            else if (parameters.Contains(" in "))
            {
                var currentReminders = Reminder.GetUserReminders(Context.User);
                if (currentReminders.Length >= 5)
                {
                    await ReplyAsync("You already have 5 reminders, which is the maximum.");
                }
                else
                {
                    string[] split = parameters.Split(new string[] { " in " }, StringSplitOptions.None);
                    string reminder = "";
                    if (split.Length == 2) reminder = split[0];
                    else
                    {
                        for (int i = 0; i < split.Length - 1; i++)
                        {
                            reminder += split[i] + " in ";
                        }
                        reminder = reminder.Substring(0,reminder.Length - 4);
                    }

                    reminder = reminder.Replace("//#//", "");

                    string time = split[split.Length - 1];
                    string[] splitTimes = time.Split(new string[] { ", and ", " and ", ", " }, StringSplitOptions.None);
                    TimeSpan remindTime = new(0, 0, 0);
                    bool stop = false;
                    foreach (string t in splitTimes)
                    {
                        var timeData = t.Split(' ');
                        if (timeData.Length > 2)
                        {
                            stop = true;
                            break;
                        }
                        var format = timeData[1].ToLower().TrimEnd('s');
                        var amount = timeData[0];

                        switch (format)
                        {
                            case "day":
                                remindTime = remindTime.Add(new TimeSpan(Convert.ToInt32(amount), 0, 0, 0));
                                break;
                            case "hour":
                                remindTime = remindTime.Add(new TimeSpan(Convert.ToInt32(amount), 0, 0));
                                break;
                            case "minute":
                            case "min":
                                remindTime = remindTime.Add(new TimeSpan(0, Convert.ToInt32(amount), 0));
                                break;
                            default:
                                stop = true;
                                break;
                        }
                    }

                    if (stop) await ReplyAsync("Invalid time format, make sure time formats are spelt correctly.");
                    else
                    {
                        DateTime remindAt = Var.CurrentDate() + remindTime;

                        _ = new Reminder(Context.User, reminder, remindAt);
                        await ReplyAsync("Reminder added.");
                    }
                }
            }
            else await ReplyAsync("Invalid format, make sure you have the word `in` with spaces on each side.");
        }

        [Command("reminderlist"), Alias(new string[] { "reminders" })]
        public async Task ReminderList()
        {
            var currentReminders = Reminder.GetUserReminders(Context.User);
            if (currentReminders.Length > 0)
            {
                string msg = "Here are your current reminders:\n```";
                for (int i = 0; i < currentReminders.Length; i++)
                {
                    msg += $"[{i + 1}]" + $"{currentReminders[i].Text} - {currentReminders[i].RemindTime.ToShortDateString()} {currentReminders[i].RemindTime.ToShortTimeString()}\n";
                }
                await ReplyAsync(msg + "\n```\nUse `;deletereminder #` to delete a reminder!");
            }
            else await ReplyAsync("You currently have no reminders.");
        }

        [Command("deletereminder"), Alias(new string[] { "delreminder" })]
        public async Task DeleteReminder(int reminderID)
        {
            var reminders = Reminder.GetUserReminders(Context.User);
            int idCount = 0;
            for (int i = 0; i < reminders.Length; i++)
            {
                idCount++;
                if (idCount == reminderID)
                {
                    reminders[i].Delete();
                    await ReplyAsync("Reminder deleted.");
                    return;
                }
                
            }
            await ReplyAsync("Reminder not found, are you sure you have a reminder with an ID of " + reminderID + "? Use `;reminderlist` to check.");
            
        }
        [Command("status")]
        public async Task Status() => await Status(Context.User);
        
        [Command("status"), Summary("See your status in ForkBot society.")]
        public async Task Status(IUser user)
        {
            int coinPos = DBFunctions.GetUserRank(user, "coins");
            var u = new User(user.Id);

            double coins = u.GetCoins();
            double totalCoins = DBFunctions.GetAllCoins();
            double percent = (coins/totalCoins)*100;

            int invValue = DBFunctions.GetInventoryValue(user);

            double itemCount = DBFunctions.GetUserItemCount(user);
            double totalItems = DBFunctions.GetTotalItemCount();
            double itemPercent = (itemCount / totalItems) * 100;

            double statCount = DBFunctions.GetUserTotalStats(user);
            double totalStats = DBFunctions.GetTotalStats();
            double statPercent = (statCount / totalStats) * 100;

            JEmbed emb = new()
            {
                Title = $"{await u.GetName(Context.Guild)}'s Status in Forkbot Society",
                ColorStripe = Functions.GetColor(user)
            };

            emb.Fields.Add(new JEmbedField(x => {
                x.Header = ":moneybag: Coins :moneybag:";
                x.Text = $"Ranked number `{coinPos}` for total coins.\n" +
                $"`{coins}` coins total, having `{percent.ToString("0.00")}%` of the global economy of `{totalCoins}` coins";
            }));
            
            emb.Fields.Add(new JEmbedField(x => {
                x.Header = ":shopping_bags: Items :shopping_bags:";
                x.Text = $"Their total inventory value (at current prices) is `{invValue}` coins.\n"+
                $"`{itemCount}` items total, which is `{(Double.IsNaN(itemPercent) ? "None" : itemPercent.ToString("0.00") + '%')}` of the total `{totalItems}` items that exist.";
            }));

            emb.Fields.Add(new JEmbedField(x => {
                x.Header = ":crown: Stats :crown:";
                x.Text = $"Total stat count is `{statCount}`, which is `{(Double.IsNaN(statPercent) ? "None" : statPercent.ToString("0.00") + '%')}` of the global total of `{totalStats}`";
            }));

            await ReplyAsync("", embed:emb.Build());
        }

        /*
        [Command("verify"), Summary("Gain access to YorkU channels!")]
        public async Task Verify([Remainder] string param)
        {
            if (Context.Channel.Id == 626164302084309002) //#landing
            {
                var reportChan = await Context.Guild.GetTextChannelAsync(Constants.Channels.REPORTED) as IMessageChannel;
                string footer = "";
                var userAccountDate = Context.User.CreatedAt;

                var reqRoles = param.Split(' ');
                var ServerRoles = Context.Guild.Roles;
                var requestedRoles = new List<IRole>();
                string unknownRoles = "";
                foreach(var role in reqRoles)
                {
                    var roleName = role.ToLower();
                    if (roleName == "york") continue;
                    int year = -1;
                    if (int.TryParse(roleName, out year))
                    {
                        if (year < 1) continue;
                        switch (year)
                        {
                            case 1:
                                roleName = "1st-year";
                                break;
                            case 2:
                                roleName = "2nd-year";
                                break;
                            case 3:
                                roleName = "3rd-year";
                                break;
                            case 4:
                                roleName = "4th-year";
                                break;
                            default:
                                roleName = "5th-year-plus";
                                break;
                        }
                    }

                    var r = ServerRoles.Where(x => x.Name.ToLower() == roleName).FirstOrDefault();
                    if (r != null)
                    {
                        requestedRoles.Add(r);
                    }
                    else
                    {
                        unknownRoles += $"`{role}`, ";
                    }
                }

                if (userAccountDate != null)
                {
                    if ((DateTime.Now-userAccountDate) < new TimeSpan(7, 0, 0, 0)) footer = "WARNING: New account.";
                }

                string text = "";
                text = $"{Context.User.Mention} has requested to be verified with the following roles:\n" + string.Join(", ", requestedRoles.Select(x => $"`{x.Name}`"));
                if (unknownRoles != "")
                {
                    text += $"\nThe following roles were requested but not found: {unknownRoles.Trim(' ').Trim(',')}";
                }

                text += $"\nUse `;verify [user]` or react with ✅ to **AUTOMATICALLY GRANT** the found roles.";
                var varMsg = await reportChan.SendMessageAsync(Context.Guild.GetRole(Constants.Roles.MOD).Mention, embed: new InfoEmbed("Verification Request", text, footer).Build());
                await varMsg.AddReactionAsync(new Emoji("✅"));
                await ReplyAsync("Moderators have recieved your verification request and will grant you access shortly.");
                Var.awaitingVerifications.Add(new AwaitingVerification(Context.User, varMsg, requestedRoles.ToArray()));
            }
        }
        */
        #endregion

        #region Item Commands


        [Command("sell"), Summary("[FUN] Sell items from your inventory.")]
        public async Task Sell(params string[] items)
        {
            var u = User.Get(Context.User);
            string msg = "";
            var itemList = DBFunctions.GetItemNameList();
            if (items.Length == 1 && items[0] == "all") await ReplyAsync("Are you sure you want to sell **all** of your items? Use `;sell allforreal` if so.");
            else if (items.Length == 1 && items[0] == "allforreal")
            {
                int coinGain = 0;
                foreach (var item in u.GetItemList())
                {
                    for (int i = 0; i < item.Value; i++) {
                        var name = DBFunctions.GetItemName(item.Key);
                        u.RemoveItem(item.Key);
                        int price = 0;
                        foreach (string line in itemList)
                        {
                            price = (int)(DBFunctions.GetItemPrice(item.Key) * Constants.Values.SELL_VAL);

                            coinGain += price;
                            break;

                        }
                    }
                }
                await u.GiveCoinsAsync(coinGain);
                msg = $"You have sold ***ALL*** of your items for {coinGain} coins.";
            }
            else
            {
                foreach (string item in items)
                {
                    if (u.HasItem(item))
                    {
                        u.RemoveItem(item);
                        int price = (int)(DBFunctions.GetItemPrice(item) * Constants.Values.SELL_VAL);

                        await u.GiveCoinsAsync(price);
                        msg += $"You successfully sold your {item} for {price} coins!\n";

                    }
                    else msg += $"You do not have an item called {item}!\n";
                }
            }

            var msgs = Functions.SplitMessage(msg);
            foreach (string m in msgs) await ReplyAsync(m);
        }

        [Command("trade"), Summary("[FUN] Initiate a trade with another user!")]
        public async Task Trade(IUser user)
        {
            if (Functions.GetTrade(Context.User) == null && Functions.GetTrade(user) == null)
            {
                if (user.Id == Context.User.Id)
                {
                    await ReplyAsync("You cannot trade yourself.");
                    return;
                }
                Var.trades.Add(new ItemTrade(Context.User, user));
                await Context.Channel.SendMessageAsync("", embed: new InfoEmbed("TRADE INVITE",
                    user.Mention + "! " + Context.User.Username + " has invited you to trade."
                    + " Type ';trade accept' to accept or ';trade deny' to deny!").Build());
            }
            else await Context.Channel.SendMessageAsync("Either you or the person you are attempting to trade with is already in a trade!"
                                                    + " If you accidentally left a trade going, use `;trade cancel` to cancel the trade.");
        }

        [Command("trade")]
        public async Task Trade(string command, string param = "")
        {
            bool showMenu = false;
            var trade = Functions.GetTrade(Context.User);
            if (trade != null)
            {
                switch (command)
                {
                    case "accept":
                        if (!trade.Accepted && Context.User.Id != trade.Starter()) trade.Accept();
                        showMenu = true;
                        break;
                    case "deny":
                        if (!trade.Accepted)
                        {
                            await Context.Channel.SendMessageAsync("", embed: new InfoEmbed("TRADE DENIED", $"<@{trade.Starter()}>, {Context.User.Username} has denied the trade request.").Build());
                            Var.trades.Remove(trade);
                        }
                        break;
                    case "add":
                        if (param != "")
                        {
                            string item = param;
                            int amount = 1;
                            if (item.Contains('*'))
                            {
                                var stuff = item.Split('*');
                                amount = Convert.ToInt32(stuff[1]);
                                item = stuff[0];
                            }
                            var success = await trade.AddItemAsync(Context.User, item, amount);
                            if (success == false)
                            {
                                if (trade.Accepted)
                                    await Context.Channel.SendMessageAsync("Unable to add item. Are you sure you have enough?");
                                else
                                    await ReplyAsync("The other user has not accepted the trade yet.");
                            }
                            else showMenu = true;
                        }
                        else await Context.Channel.SendMessageAsync("Please specify the item to add!");
                        break;
                    case "finish":
                        await trade.ConfirmAsync(Context.User);
                        if (trade.IsCompleted()) Var.trades.Remove(trade);
                        else await Context.Channel.SendMessageAsync("Awaiting confirmation from other user.");
                        break;
                    case "cancel":
                        trade.CancelAsync();
                        await Context.Channel.SendMessageAsync("", embed: new InfoEmbed("TRADE CANCELLED", $"{Context.User.Username} has cancelled the trade. All items have been returned.").Build());
                        break;
                }
            }
            else await Context.Channel.SendMessageAsync("You are not currently part of a trade.");

            if (showMenu) await Context.Channel.SendMessageAsync("", embed: await trade.CreateMenuAsync());

            if (trade.IsCompleted())
            {
                await Context.Channel.SendMessageAsync("", embed: new InfoEmbed("TRADE SUCCESSFUL", "The trade has been completed successfully.").Build());
                Var.trades.Remove(trade);
            }
        }

        [Command("donate"), Summary("[FUN] Give the specified user some of your coins or items!")]
        public async Task Donate(IUser user, int donation)
        {
            if (user.Id == Constants.Users.FORKBOT)
            {
                await ReplyAsync("You cannot donate to this user.");
                return;
            }
            int coins = donation;
            User u1 = User.Get(Context.User);
            if (donation <= 0) await ReplyAsync("Donation must be greater than 0 coins.");
            else
            {
                if (u1.GetCoins() >= coins)
                {
                    await u1.GiveCoinsAsync(-coins);
                    await User.Get(user).GiveCoinsAsync(coins);
                    await ReplyAsync($":moneybag: {user.Mention} has been given {coins} of your coins!");
                }
                else await ReplyAsync("You don't have enough coins.");
            }
        }

        [Command("give"), Summary("[FUN] Give the specified user some of your items!")]
        public async Task Give(IUser user, params string[] donation)
        {
            User u1 = User.Get(Context.User);
            User u2 = User.Get(user);

            string msg = $"{user.Mention}, {Context.User.Mention} has given you:\n";
            string donations = "";
            string fDonations = "";
            foreach (string item in donation)
            {
                if (u1.HasItem(item))
                {
                    u1.RemoveItem(item);
                    if (item == "heart")
                    {
                        u2.GiveItem("gift");
                        donations += "A gift!\n";
                    }
                    else
                    {
                        u2.GiveItem(item);
                        donations += $"{Functions.GetPrefix(item)} {item}!\n";
                    }
                }
                else fDonations += $"~~A(n) {item}~~ {Context.User.Mention}, you do not have a(n) {item}.\n";
            }

            if (donations == "") msg = $"{Context.User.Mention}, you do not have any of the inputted item(s).";
            else msg += donations += fDonations;

            await ReplyAsync(msg);
        }

        [Command("shop"), Summary("[FUN] Open the shop and buy stuff! New items each day."), Alias("buy")]
        public async Task Shop([Remainder] string item_name = null)
        {
            if (await Functions.isDM(Context.Message))
            {
                await ReplyAsync("Sorry, this command cannot be used in private messages.");
                return;
            }
            var u = User.Get(Context.User);
            DateTime day = new();
            DateTime currentDay = new();
            if (Var.currentShop != null)
            {
                day = Var.currentShop.Date();
                currentDay = Var.CurrentDate();
            }
            if (Var.currentShop == null || Math.Abs(day.Hour - currentDay.Hour) >= 4)
            {
                Var.currentShop = new Shop();
            }

            List<string> itemNames = new();
            foreach (int item in Var.currentShop.items) itemNames.Add(DBFunctions.GetItemName(item));
            var newsCount = DBFunctions.GetRelevantNewsCount();

            if (newsCount > 0)
            {
                itemNames.Add("newspaper");
            }


            if (item_name == null)
            {
                var emb = Var.currentShop.Build();
                emb.Footer.Text = $"You have: {u.GetCoins()} coins.\nTo buy an item, use `;shop [item]`.";
                await Context.Channel.SendMessageAsync("", embed: emb.Build());
            }
            else if (itemNames.Select(x => x.ToLower()).Contains(item_name.ToLower()))
            {
                if (newsCount > 0 && item_name.ToLower() == "newspaper")
                {
                    string name = "newspaper";
                    int price = DBFunctions.GetItemPrice(name);

                    if (price < 0) price *= -1;
                    if (Convert.ToInt32(u.GetCoins()) >= price)
                    {
                        await u.GiveCoinsAsync(-price);
                        u.GiveItem(name);
                        DBFunctions.AddToProperty("slot_jackpot", price);
                        await Context.Channel.SendMessageAsync($":shopping_cart: You have successfully purchased {Functions.GetPrefix(name).ToLower()} {name} {DBFunctions.GetItemEmote(name)} for {price} coins!");
                    }
                    else await Context.Channel.SendMessageAsync("Either you cannot afford this item or it is not in stock.");
                    return;
                }

                foreach (int item in Var.currentShop.items)
                {
                    var itemName = DBFunctions.GetItemName(item);
                    if (itemName.ToLower() == item_name.ToLower())
                    {
                        string name = itemName;
                        int price = DBFunctions.GetItemPrice(item);

                        if (price < 0) price *= -1;
                        int stock = Var.currentShop.stock[Var.currentShop.items.IndexOf(item)];
                        if (Convert.ToInt32(u.GetCoins()) >= price && stock > 0)
                        {
                            stock--;
                            Var.currentShop.stock[Var.currentShop.items.IndexOf(item)] = stock;
                            await u.GiveCoinsAsync(-price);
                            u.GiveItem(name);
                            await Context.Channel.SendMessageAsync($":shopping_cart: You have successfully purchased a(n) {name} {DBFunctions.GetItemEmote(name)} for {price} coins!");
                        }
                        else await Context.Channel.SendMessageAsync("Either you cannot afford this item or it is not in stock.");
                        break;
                    }
                }
            }
            else await Context.Channel.SendMessageAsync("Either something went wrong, or this item isn't in stock!");
        }

        [Command("bm")]
        public async Task BlackMarket([Remainder] string command = null)
        {
            var u = User.Get(Context.User);
            if (u.GetData<bool>("has_bm") != true) return;
            else
            {
                DateTime day = new();
                DateTime currentDay = new();
                if (Var.blackmarketShop != null)
                {
                    day = Var.blackmarketShop.Date();
                    currentDay = Var.CurrentDate();
                }
                if (Var.blackmarketShop == null || Math.Abs(day.Hour - currentDay.Hour) >= 4)
                {
                    Var.blackmarketShop = new Shop(true);
                }

                List<string> itemNames = new();
                foreach (int item in Var.blackmarketShop.items) itemNames.Add(DBFunctions.GetItemName(item));

                if (command == null)
                {
                    var emb = Var.blackmarketShop.Build();
                    emb.Footer.Text = $"You have: {u.GetCoins()} coins.\nTo buy an item, use `;shop [item]`.";
                    await Context.Channel.SendMessageAsync("", embed: emb.Build());
                }
                else if (itemNames.Select(x => x.ToLower()).Contains(command.ToLower()))
                {
                    foreach (int item in Var.blackmarketShop.items)
                    {
                        var itemName = DBFunctions.GetItemName(item);
                        if (itemName.ToLower() == command.ToLower())
                        {
                            string name = itemName;
                            string desc = DBFunctions.GetItemDescription(item);
                            int price = DBFunctions.GetItemPrice(item);
                            if (price < 0) price *= -1;
                            int stock = Var.blackmarketShop.stock[Var.blackmarketShop.items.IndexOf(item)];
                            if (Convert.ToInt32(u.GetCoins()) >= price && stock > 0)
                            {
                                stock--;
                                Var.blackmarketShop.stock[Var.blackmarketShop.items.IndexOf(item)] = stock;
                                await u.GiveCoinsAsync(-price);
                                u.GiveItem(name);
                                await Context.Channel.SendMessageAsync($":shopping_cart: You have successfully purchased a(n) {name} {DBFunctions.GetItemEmote(name)} for {price} coins!");
                            }
                            else await Context.Channel.SendMessageAsync("Either you cannot afford this item or it is not in stock.");
                        }
                    }
                }
                else await Context.Channel.SendMessageAsync("Either something went wrong, or this item isn't in stock!");
            }
        }

        [Command("freemarket"), Alias("fm", "market"), Summary("[FUN] Sell items to other users! Choose your own price!")]
        public async Task FreeMarket(params string[] command)
        {
            var user = User.Get(Context.User);
            bool sort = false, lowest = false, itemParam = false;
            if (command.Length == 0 || command[0] == "view")
            {
                int page = 1;
                if (command.Length > 0 && command[0] == "view")
                {
                    if (!int.TryParse(command[1], out page))
                    {
                        if (command[1].ToLower() == "lowest")
                        {
                            sort = true;
                            lowest = true;
                        }
                        else if (command[1].ToLower() == "highest")
                        {
                            sort = true;
                            lowest = false;
                        }
                        else itemParam = true;
                    }

                    if (command.Length >= 3) int.TryParse(command[command.Length - 1], out page);
                }



                if (page < 1) page = 1;


                var postList = await MarketPost.GetAllPostsAsync();

                IOrderedEnumerable<MarketPost> sortedList;
                MarketPost[] posts = postList.ToArray();

                if (sort)
                {
                    if (lowest) sortedList = postList.OrderBy(x => x.Price);
                    else sortedList = postList.OrderByDescending(x => x.Price);
                    posts = sortedList.ToArray();
                }

                if (itemParam)
                {
                    string searchedItem = command[1];
                    posts = posts.Where(x => x.Item_ID == DBFunctions.GetItemID(searchedItem)).ToArray();
                }


                const int ITEMS_PER_PAGE = 10;

                JEmbed emb = new()
                {
                    Title = "Free Market",
                    Description = "To buy an item, use ;fm buy [ID]! For more help and examples, use ;fm help."
                };
                double pageCount = Math.Ceiling((double)posts.Length / ITEMS_PER_PAGE);
                emb.Footer.Text = $"Page {page}/{pageCount}";
                emb.ColorStripe = Constants.Colours.YORK_RED;

                page -= 1;

                int itemStart = ITEMS_PER_PAGE * page;
                int itemEnd = itemStart + ITEMS_PER_PAGE;

                if (itemStart > posts.Length)
                {
                    await ReplyAsync("Invalid page number.");
                    return;
                }

                if (itemEnd > posts.Length) itemEnd = posts.Length;

                if (posts.Length == 0)
                {
                    emb.Fields.Add(new JEmbedField(x =>
                    {
                        x.Header = "Either the Free Market is empty, or no items match your parameters!";
                        x.Text = "Sorry!";
                    }));
                    emb.Footer.Text = "Page 0/0";
                }

                for (int i = itemStart; i < itemEnd; i++)
                {
                    string id = posts[i].ID;
                    var seller = posts[i].User;
                    string itemName = DBFunctions.GetItemName(posts[i].Item_ID);
                    int amount = Convert.ToInt32(posts[i].Amount);
                    int price = Convert.ToInt32(posts[i].Price);

                    string plural = "";
                    if (amount > 1)
                    {
                        if (itemName.EndsWith("es")) plural = "";
                        else if (itemName.EndsWith("ss")) plural = "es";
                        else if (itemName.EndsWith("s")) plural = "";
                        else plural = "s";
                    }


                    emb.Fields.Add(new JEmbedField(x =>
                    {
                        x.Header = $"{DBFunctions.GetItemEmote(itemName)} ({amount}) {itemName}{plural} - id: {id}";
                        x.Text = $"{Constants.Emotes.BLANK}:moneybag: {price} Coins\n{Constants.Emotes.BLANK}";
                        x.Inline = false;
                    }));
                }

                await ReplyAsync("", embed: emb.Build());
            }
            else if (command[0] == "help")
            {
                await ReplyAsync("Free Market Help!\n\n" +
                                 "To view the Free Market, use either `;fm` or `;fm view`. You can do `;fm view [page #]` to view other pages.\n" +
                                 "You can also use certain parameters, `lowest`, `highest`, and `[itemname]` to narrow down or sort the Free Market.\n" +
                                 "To buy an item in the free market, use `;fm buy [ID]`. The ID is the characters that appear in the title of the sale in `;fm`\n" +
                                 "To post an item for sale, do ;fm post [item] [price]. You can also include the amount of items you want to sell in the format `[item]*[amount]`\n" +
                                 "To cancel a posting, use `;fm cancel [ID]`\nThere is a 25% coin fee for cancelling posts in order to avoid abuse. This will be automatically charged upon cancellation, if you cannot afford the fee, you cannot cancel.\n" +
                                 "There is a 5 posting limit.\n" +
                                 "There is a 2 week expiry on postings. You will be given a warning before your posting expires, and if the posting is not removed by the expiry time it will be auctioned off to other users and the coins will go towards the slots, **not you**.\n\n" +
                                 "Examples:\n\n" +
                                 "`;fm view 3` Views the third Free Market page.\n" +
                                 "`;fm view lowest` Views all items sorted by the lowest price.\n" +
                                 "`;fm view key 5` Views the fifth page of just keys.\n" +
                                 "`;fm post apple 100` Posts 1 apple for sale for 100 coins.\n" +
                                 "`;fm post gun*10 7500` Posts 10 guns for sale for 7500 coins.\n" +
                                 "`;fm buy A1B2C3` buys an item with the ID `A1B2C3`.\n\n" +
                                 "If something still doesn't make sense, just ask Brady.");
            }
            else if (command[0] == "post")
            {
                string[] itemData = command[1].Split('*');

                int amount;
                if (itemData.Length == 1) amount = 1;
                else int.TryParse(itemData[1], out amount);

                if (amount < 1)
                {
                    await ReplyAsync("You must be posting at least one item.");
                    return;
                }

                string item = itemData[0];
                int price = int.Parse(command[2]);
                if (price < 1)
                {
                    await ReplyAsync("You must be charging at least 1 coin.");
                    return;
                }

                var itemID = DBFunctions.GetItemID(item);
                if (!user.HasItem(itemID, amount))
                {
                    await ReplyAsync(":x: You either do not have the item, or enough of the item in your inventory. :x:");
                    return;
                }

                if ((await MarketPost.GetPostsByUser(Context.User.Id)).Length >= 10)
                {
                    await ReplyAsync(":x: You've reached the maximum of 10 Free Market postings.");
                    return;
                }

                string plural = "";
                if (price > 1) plural = "s";

                MarketPost mp = new MarketPost(Context.User, itemID, amount, price, Var.CurrentDate());

                for (int i = 0; i < amount; i++) user.RemoveItem(item);

                var expiryDate = Var.CurrentDate() + new TimeSpan(14, 0, 0, 0);
                await ReplyAsync($"You have successfully posted {amount} {item}(s) for {price} coin{plural}. The sale ID is {mp.ID}.\n" +
                    $"The posting will expire on {CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(expiryDate.Month)} {expiryDate.Day}.");
            }
            else if (command[0] == "buy")
            {
                string id = command[1].ToUpper();
                var post = await MarketPost.GetPostAsync(id);


                if (post.User.Id == Context.User.Id)
                {
                    await ReplyAsync(":x: You cannot purchase your own posting. :x:");
                    return;
                }
                string itemName = DBFunctions.GetItemName(post.Item_ID);
                int amount = post.Amount;
                int price = post.Price;

                if (user.GetCoins() >= price)
                {
                    for (int o = 0; o < amount; o++) user.GiveItem(itemName);
                    await user.GiveCoinsAsync(-price);

                    MarketPost.DeletePost(id);

                    string plural = "";
                    if (amount > 1) plural = "s";

                    string pluralC = "";
                    if (price > 1) pluralC = "s";

                    await ReplyAsync($"You have successfully purchased {amount} {itemName}{plural} for {price} coin{pluralC}!");
                    await User.Get(post.User).GiveCoinsAsync(price);
                    await post.User.SendMessageAsync($"{Context.User.Username}#{Context.User.Discriminator} has purchased your {amount} {itemName}{plural} for {price} coin{pluralC}.");

                }
                else await ReplyAsync(":x: You cannot afford this posting. :x:");


            }
            else if (command[0] == "cancel")
            {
                var id = command[1].ToUpper();
                var item = await MarketPost.GetPostAsync(id);

                if (item == null) await ReplyAsync("Post with ID " + id + " not found.");
                else
                {
                    ulong sellerID = item.User.Id;
                    if (item.ID == id)
                    {
                        if (sellerID == Context.User.Id)
                        {
                            int fee = (int)(item.Price * Constants.Values.MARKET_CANCELLATION_FEE);
                            if (user.GetCoins() >= fee)
                            {
                                await user.GiveCoinsAsync(-fee);
                                for (int o = 0; o < item.Amount; o++) user.GiveItem(item.Item_ID);
                                MarketPost.DeletePost(item.ID);
                                await ReplyAsync($"You have successfully canceled your posting of {item.Amount} {DBFunctions.GetItemName(item.Item_ID)}(s). They have returned to your inventory and you have been charged the cancellation fee of {fee} coins.");
                            }
                            else await ReplyAsync($"You cannot afford the cancellation fee of {fee} coins and have not cancelled this posting.");
                        }
                        else
                        {
                            await ReplyAsync(":x: You cannot cancel someone elses posting! :x:");
                        }
                    }
                }
            }
        }

        [Command("iteminfo"), Summary("Its like a pokedex but for items!")]
        public async Task ItemInfo([Remainder] string item)
        {
            var itemID = DBFunctions.GetItemID(item);
            if (itemID == -1)
                await ReplyAsync($"Item '{item}' does not exist.");
            else
            {
                JEmbed emb = new()
                {
                    Title = DBFunctions.GetItemEmote(item) + " " + DBFunctions.GetItemName(itemID),
                    Description = DBFunctions.GetItemDescription(itemID),
                    ColorStripe = Constants.Colours.YORK_RED
                };
                if (!DBFunctions.ItemIsShoppable(itemID)) emb.Description += $"\n\n:moneybag: Cannot be purchased. Find through presents or combining!\nSell: {Convert.ToInt32(DBFunctions.GetItemPrice(itemID) * Constants.Values.SELL_VAL)} coins.";
                else emb.Description += $"\n\n:moneybag: Buy: {DBFunctions.GetItemPrice(itemID)} coins.\nSell: {Convert.ToInt32(DBFunctions.GetItemPrice(itemID) * Constants.Values.SELL_VAL)} coins.";
                emb.Footer.Text = $"There are currently {DBFunctions.GetTotalItemCount(item)} in circulation.";
                await ReplyAsync("", embed: emb.Build());
            }
        }

        [Command("combine"), Summary("[FUN] Combine lame items to make rad items!")]
        public async Task Combine(params string[] items)
        {
            User u = User.Get(Context.User);

            foreach (string item in items)
            {
                if (!u.HasItem(item))
                {
                    await ReplyAsync($"You do not have a(n) {item}!");
                    return;
                }
            }


            string result = ItemCombo.CheckCombo(items);

            if (result != null)
            {
                if (result.StartsWith("special:"))
                {
                    var spec = result.Split(':')[1];
                    switch (spec)
                    {
                        case "oldbm":
                            await ReplyAsync(":spy: Sorry... We ain't accepting that no more. I might take somethin shinier though.");
                            break;
                    }
                }
                else {
                    foreach (string item in items) u.RemoveItem(item);
                    u.GiveItem(result);
                    await ReplyAsync($"You have successfully made a {result}! " + DBFunctions.GetItemEmote(result));
                }
            }
            else
            {
                await ReplyAsync("This combo doesn't exist!");
            }
        }

        [Command("trash"), Summary("[FUN] Throw items away.")]
        public async Task Trash(params string[] items)
        {
            var u = User.Get(Context.User);
            string msg = "";
            foreach (string item in items)
            {
                if (u.HasItem(item))
                {
                    u.RemoveItem(item);
                    msg += ":recycle: You have successfully thrown away your " + item + "!\n";
                }
                else msg += ":x: You do not have an item called " + item + "!\n";
            }
            var msgs = Functions.SplitMessage(msg);
            foreach (string m in msgs) await ReplyAsync(m);
        }

        [Command("bid"), Alias("auction")]
        public async Task Bid(params string[] commands)
        {
            if (await Functions.isDM(Context.Message))
            {
                await ReplyAsync("This command can not be used in direct messages.");
                return;
            }

            var bids = ForkBot.Bid.GetAllBids();
            if (commands.Length == 0) commands = new string[] { "" };

            var notifyUserIDs = DBFunctions.GetUserIDsWhere("Notify_Bid", "1");

            switch (commands[0])
            {
                case "":
                    JEmbed emb = new()
                    {
                        Title = "Auctions",
                        ColorStripe = Constants.Colours.YORK_RED
                    };
                    emb.Footer.Text = "To bid, use `;bid [ID] [Amount]`";
                    if (!notifyUserIDs.Contains(Context.User.Id)) emb.Footer.Text += " \n Want to be notified when there's a new auction? Use `;bid opt-in`!";

                    if (bids.Length == 0) emb.Description = "There are currently no auctions going on.";
                    foreach (var bid in bids)
                    {
                        var id = bid.ID;
                        var item = DBFunctions.GetItemName(bid.Item_ID);
                        var itemCount = bid.Amount;
                        var endDate = bid.EndDate;
                        var currentBidAmount = bid.CurrentBid;
                        var bidder = bid.CurrentBidder;
                        string bidderMsg = "";
                        var endTime = endDate - Var.CurrentDate();


                        if (bidder != 0)
                        {
                            var u = User.Get(bid.CurrentBidder);
                            bidderMsg += $" by {await u.GetName(Context.Guild)}";
                        }

                        emb.Fields.Add(new JEmbedField(x =>
                        {
                            x.Header = $"{DBFunctions.GetItemEmote(item)} ({itemCount}) {item} - id: {id}";
                            var text = $"{Constants.Emotes.BLANK} :moneybag: Current bid: **{currentBidAmount}** coins{bidderMsg}\n" +
                                       $"{Constants.Emotes.BLANK} Minimum Next Bid: **{Math.Ceiling(currentBidAmount + currentBidAmount * 0.15)}** coins.\n";

                            if (endTime.Hours < 1) text += $"{Constants.Emotes.BLANK} Ending in: **{endTime.Minutes}** minutes and **{endTime.Seconds}** seconds.";
                            else text += $"{Constants.Emotes.BLANK} Ending in: **{endTime.Hours}** hours and **{endTime.Minutes}** minutes.";
                            x.Text = text;

                        }));
                    }
                    await ReplyAsync("", embed: emb.Build());
                    break;
                case "opt-in":
                case "opt-out":
                    bool optedIn = notifyUserIDs.Contains(Context.User.Id);
                    var user = User.Get(Context.User);
                    if (optedIn)
                    {
                        user.SetData("Notify_Bid", false);
                        await ReplyAsync("Successfully opted-out of bid notifications.");
                    }
                    else
                    {
                        user.SetData("Notify_Bid", true);
                        await ReplyAsync("Successfully opted-in to bid notifications.");
                    }

                    break;
                default:
                    string bidID = commands[0];
                    int bidAmount = Convert.ToInt32(commands[1]);
                    var BID = ForkBot.Bid.GetBid(bidID);
                    if (BID == null)
                    {
                        await ReplyAsync("Bid ID not found. Make sure you've typed it correctly.");
                        return;
                    }


                    var itemID = BID.Item_ID;
                    var amount = BID.Amount;
                    var currentBid = BID.CurrentBid;

                    if (bidAmount > currentBid)
                    {
                        var u = User.Get(Context.User);
                        if (BID.CurrentBidder == 0 || u.ID != BID.CurrentBidder)
                        {
                            if (u.GetCoins() >= bidAmount)
                            {
                                if (bidAmount >= Math.Ceiling(currentBid + (currentBid * 0.15)))
                                {
                                    await u.GiveCoinsAsync(-bidAmount);
                                    if (BID.CurrentBidder != 0)
                                    {
                                        var oldUser = User.Get(BID.CurrentBidder);
                                        await oldUser.GiveCoinsAsync(currentBid);
                                    }
                                    BID.Update(Context.User, bidAmount);
                                    
                                    string timeExtend = "";
                                    var endDate = BID.EndDate;
                                    var endTime = endDate - Var.CurrentDate();
                                    if (endTime < new TimeSpan(0, 3, 0))
                                    {
                                        BID.AddTime(new TimeSpan(0, 1, 0));
                                        timeExtend = "\nThe end time has been extended by 1 minute.";
                                    }

                                    await ReplyAsync($"You are now the highest bidder for {DBFunctions.GetItemEmote(itemID)} {amount} {DBFunctions.GetItemName(itemID)}(s) with {bidAmount} coins.{timeExtend}");

                                    break;
                                }
                                else await ReplyAsync($"Your bid must be at least 15% higher than the current. ({Math.Ceiling(currentBid + currentBid * 0.15)} coins)");
                            }
                            else await ReplyAsync("You do not have the specified amount of coins.");
                        }
                        else await ReplyAsync("You already have the highest bid for this item.");
                    }
                    else await ReplyAsync("Your bid must be higher than the current bid.");

                    break;
            }
        }
        #endregion

        #region Fun

        //viewing tag
        [Command("tag"), Summary("Make or view a tag!")]
        public async Task Tag(string tag)
        {
            if (Context.Guild.Id == Constants.Guilds.YORK_UNIVERSITY) return;
            if (!File.Exists("Files/tags.txt")) File.Create("Files/tags.txt").Dispose();
            string[] tags = File.ReadAllLines("Files/tags.txt");
            bool sent = false;
            string msg = "";
            foreach (string line in tags)
            {
                if (tag == "list")
                {
                    msg += "\n" + line.Split('|')[0];
                }
                else if (line.Split('|')[0] == tag)
                {
                    sent = true;
                    await Context.Channel.SendMessageAsync(line.Split('|')[1]);
                    break;
                }
            }

            if (tag == "list")
            {
                var msgs = Functions.SplitMessage(msg);
                foreach (string message in msgs)
                {
                    await ReplyAsync($"```\n{message}\n```");
                }
            }
            else if (!sent) await Context.Channel.SendMessageAsync("Tag not found!");

        }

        //for creating the tag
        [Command("tag")]
        public async Task Tag(string tag, [Remainder]string content)
        {
            if (Context.Guild.Id == Constants.Guilds.YORK_UNIVERSITY) return;
            if (!File.Exists("Files/tags.txt")) File.Create("Files/tags.txt").Dispose();
            bool exists = false;
            if (tag == "list") exists = true;
            else if (tag == "delete" && Context.User.Id == Constants.Users.BRADY)
            {
                var tags = File.ReadAllLines("Files/tags.txt").ToList();
                for (int i = 0; i < tags.Count; i++)
                {
                    if (tags[i].Split('|')[0] == content) { tags.Remove(tags[i]); break; }
                }
                File.WriteAllLines("Files/tags.txt", tags);
            }
            else
            {
                string[] tags = File.ReadAllLines("Files/tags.txt");
                foreach (string line in tags)
                {
                    if (line.Split('|')[0] == tag) 
                    { 
			            exists = true;
			            break;
		            }
                }

                if (!exists)
                {
                    File.AppendAllText("Files/tags.txt", tag + "|" + content + "\n");
                    await Context.Channel.SendMessageAsync("Tag created!");
                }
                else await Context.Channel.SendMessageAsync("Tag already exists!");
            }
        }


        [Command("allowance"), Summary("[FUN] Receive your daily free coins.")]
        public async Task Allowance()
        {
            if (await Functions.isDM(Context.Message))//
            {
                await ReplyAsync("Sorry, this command cannot be used in private messages.");
                return;
            }
            var u = User.Get(Context.User);
            var lastAllowance = u.GetData<DateTime>("allowance_datetime");

            var ONE_DAY = new TimeSpan(24, 0, 0);
            if ((lastAllowance + ONE_DAY) < DateTime.Now)
            {
                double allowance = rdm.Next(100, 500);
                if (u.HasItem("credit_card")) allowance *= 2.5;
                int iallowance = (int)allowance;
                await u.GiveCoinsAsync(iallowance);
                u.SetData("allowance_datetime", DateTime.Now);
                await Context.Channel.SendMessageAsync($":moneybag: | Here's your daily allowance! ***+{iallowance} coins.*** The next one will be available in 24 hours.");
            }
            else
            {
                var next = (lastAllowance + ONE_DAY) - DateTime.Now;
                await Context.Channel.SendMessageAsync($"Your next allowance will be available in {next.Hours} hours, {next.Minutes} minutes, and {next.Seconds} seconds.");
            }
        }

        [Command("hangman"), Summary("[FUN] Play a game of Hangman with the bot."), Alias(new string[] { "hm" })]
        public async Task HangMan()
        {
            if (!Var.hangman)
            {
                var wordList = File.ReadAllLines("Files/wordlist.txt");
                Var.hmWord = wordList[(rdm.Next(wordList.Length))].ToLower();
                Var.hangman = true;
                Var.hmCount = 0;
                Var.hmErrors = 0;
                Var.guessedChars.Clear();
                await HangMan("");
            }
            else
            {
                await Context.Channel.SendMessageAsync("There is already a game of HangMan running.");
            }
        }

        //this code is terrible
        [Command("hangman"), Alias(new string[] { "hm" })]
        public async Task HangMan(string guess)
        {
            if (Var.hangman)
            {
                guess = guess.ToLower();
                if (guess != "" && Var.guessedChars.Contains(guess[0]) && guess.Length == 1) await Context.Channel.SendMessageAsync("You've already guessed " + Char.ToUpper(guess[0]));
                else
                {
                    if (guess.Length == 1 && !Var.guessedChars.Contains(guess[0])) Var.guessedChars.Add(guess[0]);
                    if (guess != "" && ((!Var.hmWord.Contains(guess[0]) && guess.Length == 1) || (Var.hmWord != guess && guess.Length > 1))) Var.hmErrors++;


                    string[] hang = {
                        ".      ______    " ,    //0
                        "      /      \\  " ,    //1
                        "     |           " ,    //2
                        "     |           " ,    //3
                        "     |           " ,    //4
                        "     |           " ,    //5
                        "     |           " ,    //6
                        "_____|_____      " };   //7


                    for (int i = 0; i < Var.hmWord.Length; i++)
                    {
                        if (Var.guessedChars.Contains(Var.hmWord[i])) hang[6] += Char.ToUpper(Convert.ToChar(Var.hmWord[i])) + " ";
                        else hang[6] += "_ ";
                    }

                    for (int i = 0; i < Var.hmErrors; i++)
                    {
                        if (i == 0)
                        {
                            var line = hang[2].ToCharArray();
                            line[13] = 'O';
                            hang[2] = new string(line);
                        }
                        if (i == 1)
                        {
                            var line = hang[3].ToCharArray();
                            line[13] = '|';
                            hang[3] = new string(line);
                        }
                        if (i == 2)
                        {
                            var line = hang[4].ToCharArray();
                            line[12] = '/';
                            hang[4] = new string(line);
                        }
                        if (i == 3)
                        {
                            var line = hang[4].ToCharArray();
                            line[14] = '\\';
                            hang[4] = new string(line);
                        }
                        if (i == 4)
                        {
                            var line = hang[3].ToCharArray();
                            line[12] = '/';
                            hang[3] = new string(line);
                        }
                        if (i == 5)
                        {
                            var line = hang[3].ToCharArray();
                            line[14] = '\\';
                            hang[3] = new string(line);
                        }
                    }

                    if (!hang[6].Contains('_') || Var.hmWord == guess) //win
                    {
                        Var.hangman = false;
                        foreach (char c in Var.hmWord)
                        {
                            Var.guessedChars.Add(c);
                        }
                        var u = User.Get(Context.User);
                        char[] vowels = { 'a', 'e', 'i', 'o', 'u' };
                        int vowelCount = Var.hmWord.Where(x=> vowels.Contains(x)).Count();
                        int constCount = Var.hmWord.Length - vowelCount;
                        int coinReward = (vowelCount * 5) + (constCount * 10) - (Var.hmErrors * 3) + rdm.Next(15);
                        
                        await u.GiveCoinsAsync(coinReward);
                        await Context.Channel.SendMessageAsync($"You did it! You got {coinReward} coins.");
                    }

                    hang[6] = "     |          ";
                    for (int i = 0; i < Var.hmWord.Length; i++)
                    {
                        if (Var.guessedChars.Contains(Var.hmWord[i])) hang[6] += Char.ToUpper(Convert.ToChar(Var.hmWord[i])) + " ";
                        else hang[6] += "_ ";
                    }

                    if (Var.hmErrors == 6)
                    {
                        await Context.Channel.SendMessageAsync("You lose! The word was: " + Var.hmWord);
                        Var.hangman = false;
                    }

                    string msg = "```\n";
                    foreach (string s in hang) msg += s + "\n";
                    msg += "```";
                    if (Var.hangman)
                    {
                        msg += "Guessed letters: ";
                        foreach (char c in Var.guessedChars) msg += char.ToUpper(c) + " ";
                        msg += "\nUse `;hangman [guess]` to guess a character or the entire word.";

                    }
                    await Context.Channel.SendMessageAsync(msg);
                }
            }
            else
            {
                await HangMan();
                await HangMan(guess);
            }
        }

        [Command("profile"), Summary("View your or another users profile.")]
        public async Task Profile([Remainder] IUser user)
        {
            var u = User.Get(user);

            JEmbed emb = new();
            emb.Author.Name = user.Username;
            emb.Author.IconUrl = user.GetAvatarUrl();

            emb.ColorStripe = Functions.GetColor(user);

            emb.Fields.Add(new JEmbedField(x =>
            {
                x.Header = "Coins:";
                x.Text = u.GetCoins().ToString();
                x.Inline = true;
            }));


            var gUser = (user as IGuildUser);
            if (gUser != null && gUser.RoleIds.Count > 1)
            {
                emb.Fields.Add(new JEmbedField(x =>
                {
                    x.Header = "Roles:";
                    string text = "";

                    foreach (ulong id in gUser.RoleIds)
                    {
                        if (Context.Guild.GetRole(id).Name != "@everyone")
                            text += Context.Guild.GetRole(id).Name + ", ";
                    }

                    x.Text = Convert.ToString(text).Trim(' ', ',');
                    x.Inline = true;
                }));
            }

            var items = u.GetItemList();

            List<string> fields = new();
            string txt = "";
            foreach (KeyValuePair<int, int> item in items)
            {
                string itemListing = $"{DBFunctions.GetItemEmote(item.Key)} {DBFunctions.GetItemName(item.Key)} ";
                if (item.Value > 1) itemListing += $"x{item.Value} ";
                if (txt.Length + itemListing.Length > 1024)
                {
                    fields.Add(txt);
                    txt = itemListing;
                }
                else txt += itemListing;
            }
            fields.Add(txt);

            string title = "Inventory";
            foreach (string f in fields)
            {
                emb.Fields.Add(new JEmbedField(x =>
                {
                    x.Header = title + ":";
                    x.Text = f;
                }));
                title += " (cont.)";
            }


            emb.Fields.Add(new JEmbedField(x =>
            {
                x.Header = "Stats:";
                string text = "";
                foreach (var stat in u.GetStats())
                {
                    if (stat.Value != 0)
                        text += stat.Key + ": " + stat.Value + "\n";
                }
                x.Text = Convert.ToString(text);
            }));

            await Context.Channel.SendMessageAsync("", embed: emb.Build());
        }

        [Command("profile")]
        public async Task Profile() => await Profile(Context.User);

        [Command("present"), Summary("[FUN] Get a cool gift!")]
        public async Task Present()
        {
            if (await Functions.isDM(Context.Message))
            {
                await ReplyAsync("Sorry, this command cannot be used in private messages.");
                return;
            }
            if (!Var.presentWaiting)
            {
                if (Var.presentTime < Var.CurrentDate() - Var.presentWait)
                {
                    Var.presentCount = rdm.Next(4) + 1;
                    Var.presentClaims.Clear();
                }

                User user = User.Get(Context.User);
                bool hasPouch = user.GetData<bool>("Active_Pouch");
                if (Var.presentCount > 0 && (!Var.presentClaims.Any(x => x.Id == Context.User.Id) || hasPouch))
                {
                    if (Var.presentClaims.Count <= 0)
                    {
                        Var.presentWait = new TimeSpan(rdm.Next(4), rdm.Next(60), rdm.Next(60));
                        Var.presentTime = Var.CurrentDate();
                    }

                    if (Var.presentClaims.Any(x => x.Id == Context.User.Id) && hasPouch)
                        user.SetData("active_pouch", "0");


                    Var.presentCount--;
                    if (Context.User is IGuildUser gUser)
                        Var.presentClaims.Add(gUser);
                    else
                        Var.presentClaims.Add(Context.User);
                    Var.presentNum = rdm.Next(10);
                    
                    await Context.Channel.SendMessageAsync($"A present appears! :gift: Press {Var.presentNum} to open it!");
                    Var.presentWaiting = true;
                    Var.replacing = false;
                    Var.replaceable = true;
                    


                }
                else
                {
                    var timeLeft = Var.presentTime - (Var.CurrentDate() - Var.presentWait);
                    var msg = $"The next presents are not available yet! Please be patient! They should be ready in *about* {timeLeft.Hours + 1} hour(s)!";

                    var claims = DBFunctions.GetProperty("Record_Claims");
                    int record = Convert.ToInt32(claims);

                    if (Var.presentClaims.Count > record) DBFunctions.SetProperty("Record_Claims", Var.presentClaims.Count.ToString());
                    msg += $"\nThere have been {Var.presentClaims.Count} claims! The record is {DBFunctions.GetProperty("Record_Claims")}.";
                    
                    if (Var.presentClaims.Count > 0)
                    {
                        msg += "\nLast claimed by:\n```\n";
                        foreach (IUser u in Var.presentClaims)
                        {
                            var gUser = u as IGuildUser;
                            msg += $"\n{gUser.Username} in {gUser.Guild}";
                        }
                        msg += "\n```";
                    }
                    msg += $"There are {Var.presentCount} presents left!";
                    await Context.Channel.SendMessageAsync(msg);
                }
            }
        }

        [Command("poll"), Summary("[FUN] Create a poll for users to vote on.")]
        public async Task Poll(string command = "", params string[] parameters)
        {
            if (command == "")
            {
                if (Var.currentPoll != null && !Var.currentPoll.completed) await Context.Channel.SendMessageAsync("", embed: Var.currentPoll.GenerateEmbed());
                else await Context.Channel.SendMessageAsync("There is currently no poll. Create one with `;poll create [question] [option1] [option2] etc..`");
            }
            else if (command == "create")
            {
                if (Var.currentPoll == null || Var.currentPoll.completed)
                {
                    Var.currentPoll = new Poll(Context.Channel, 5, parameters[0], parameters.Where(x => x != parameters[0]).ToArray());
                    await Context.Channel.SendMessageAsync("Poll created!", embed: Var.currentPoll.GenerateEmbed());
                }
                else await Context.Channel.SendMessageAsync("There is already a poll running.");
            }
            else if (command == "vote")
            {
                if (Var.currentPoll != null && !Var.currentPoll.completed)
                {
                    if (!Var.currentPoll.HasVoted(Context.User.Id))
                    {
                        Var.currentPoll.Vote(Char.Parse(parameters[0]));
                        Var.currentPoll.voted.Add(Context.User.Id);
                        await Context.Channel.SendMessageAsync("You have successfully voted for option " + parameters[0] + ".");
                    }
                    else await Context.Channel.SendMessageAsync("You have already voted!");
                }
                else await Context.Channel.SendMessageAsync("There is currently no poll.");
            }
        }

        [Command("top"), Summary("[FUN] View the top users of ForkBot based on their stats. Use a stat as the parameter in order to see specific stat rankings.")]
        public async Task Top([Remainder] string stat = "")
        {

            using (var con = new SQLiteConnection(Constants.Values.DB_CONNECTION_STRING))
            {
                con.Open();

                var stm = "";
                //string title = "";
                string emote = "";

                JEmbed emb = new()
                {
                    ColorStripe = Constants.Colours.DEFAULT_COLOUR
                };

                string order = "DESC";
                if (stat.Split(' ')[0].ToLower() == "bottom")
                {
                    stat = stat.Split(' ')[1].ToLower();
                    order = "";
                }

                if (stat == "coin" || stat == "coins")
                {
                    stm = $"SELECT USER_ID, COINS FROM USERS WHERE COINS <> 0 AND USER_ID != {Constants.Users.FORKBOT} ORDER BY COINS {order}";
                    emb.Title = "Top 5 Richest Users";
                    emote = "💰";
                }
                else if (DBFunctions.GetItemID(stat) != -1)
                {
                    var id = DBFunctions.GetItemID(stat);
                    stm = $"SELECT USER_ID, COUNT FROM USER_ITEMS WHERE ITEM_ID = {id} AND USER_ID != {Constants.Users.FORKBOT} ORDER BY COUNT {order}";
                    emb.Title = $"Top 5 Most {DBFunctions.GetItemName(id)}s";
                    emote = DBFunctions.GetItemEmote(id);
                }
                else if (stat == "item" || stat == "items")
                {
                    stm = $"SELECT USER_ID, SUM(count) FROM USER_ITEMS WHERE USER_ID != {Constants.Users.FORKBOT} GROUP BY USER_ID ORDER BY SUM(COUNT) {order} LIMIT 5";
                    emb.Title = "Top 5 Most Materialistic Users";
                    emote = "🛍";
                }
                else if (DBFunctions.StatExists(stat)) //specific stat
                {
                    stm = $"SELECT USER_ID, {stat} FROM USER_STATS WHERE USER_ID != {Constants.Users.FORKBOT} ORDER BY {stat} {order} LIMIT 10";
                    emb.Title = $"Top 5 {stat.ToTitleCase()}";
                    emote = "📈";
                }
                else if (stat == "stat" || stat == "stats") //all stats
                {
                    var stats = string.Join('+',DBFunctions.GetAllStats());
                    stm = $"SELECT USER_ID, {stats} FROM USER_STATS WHERE USER_ID != {Constants.Users.FORKBOT} ORDER BY {stats} {order} LIMIT 10";
                    emb.Title = "Top 5 Total Stats";
                    emote = "👑";
                }
                else
                {
                    await ReplyAsync("Invalid argument. Possible toplists are:\n`coins`, `items`, `stats`, `[item_name]`, `[stat]`");
                    return;
                }

                using (var com = new SQLiteCommand(stm, con))
                {
                    var reader = com.ExecuteReader();
                    int rank = 0;
                    while (reader.Read() && rank < 5)
                    {
                        rank++;
                        
                        var userID = Convert.ToUInt64(reader.GetInt64(0));
                        var user = User.Get(userID);
                        var name = await user.GetName(Context.Guild);
                        emb.Fields.Add(new JEmbedField(x =>
                        {
                            if (name == null)
                                rank--;
                            else
                            {
                                int value = reader.GetInt32(1);

                                x.Header = $"[{rank}] {name}";
                                x.Text = $"{emote} {value}";


                                x.Inline = false;
                            }
                        }));
                    }

                    await ReplyAsync("", embed: emb.Build());
                }
            }

        }

        [Command("lottery"), Alias("lotto"), Summary("[FUN] The Happy Lucky Lottery! Buy a lotto card and check daily to see if your numbers match!")]
        public async Task Lottery(string command = "")
        {
            if (await Functions.isDM(Context.Message))
            {
                await ReplyAsync("Sorry, this command cannot be used in private messages.");
                return;
            }
            User u = User.Get(Context.User);

            if (command == "")
            {
                var currentDay = Var.CurrentDate();
                if (Var.lottoDay.DayOfYear < currentDay.DayOfYear || Var.lottoDay.Year < currentDay.DayOfYear)
                {
                    Var.lottoDay = Var.CurrentDate();
                    Var.todaysLotto = $"{rdm.Next(10)}{rdm.Next(10)}{rdm.Next(10)}{rdm.Next(10)}";
                }

                JEmbed emb = new()
                {
                    Title = "Happy Lucky Lottery",
                    Description = "It's the Happy Lucky Lottery!\nMatch any of todays digits with your number to win prizes!\n\n" +
                                  "Todays number: " + Var.todaysLotto,
                    ColorStripe = Functions.GetColor(Context.User)
                };

                string uNum = u.GetData<string>("lotto_num");
                string uNum2 = u.GetData<string>("bm_lotto_num");

                if (uNum == null || uNum == "0") emb.Footer.Text = "Get your number today with ';lottery buy'!";
                else
                {
                    var lottoDay = u.GetData<DateTime>("lotto_day").AddHours(5);
                    if (lottoDay.DayOfYear >= currentDay.DayOfYear && lottoDay.Year == currentDay.Year) emb.Description += "\n\nYou've already checked the lottery today! Come back tomorrow!";
                    else
                    {

                        u.SetData("lotto_day", currentDay);
                        int matchCount = 0;
                        for (int i = 0; i < 4; i++)
                        {
                            if (uNum[i] == Var.todaysLotto[i]) matchCount++;
                        }

                        int matchCount2 = 0;
                        if (uNum2 != null && uNum2 != "0")
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                if (uNum2[i] == Var.todaysLotto[i]) matchCount2++;
                            }
                        }

                        if (matchCount2 > matchCount)
                        {
                            matchCount = matchCount2;
                            uNum = uNum2;
                        }

                        emb.Fields.Add(new JEmbedField (async x =>
                        {
                            x.Header = "Matches";
                            x.Text = $"You got {matchCount} match(es)!";
                            if (matchCount == 0) x.Text += "\nSorry!";
                            else
                            {
                                x.Text += "\nCongratulations! ";
                                switch (matchCount)
                                {
                                    case 1:
                                        x.Text += "You got 1000 coins!";
                                        await u.GiveCoinsAsync(1000);
                                        break;
                                    case 2:
                                        string[] level2Items = { "gift", "key", "moneybag", "ticket", "gift" };
                                        string item = level2Items[rdm.Next(level2Items.Length)];
                                        x.Text += $"You got 2500 coins and a(n) {item} {DBFunctions.GetItemEmote(item)}!";
                                        await u.GiveCoinsAsync(2500);
                                        u.GiveItem(item);
                                        break;
                                    case 3:
                                        string[] level3Items = { "key", "key", "calling", "gun", "unicorn", "moneybag", "moneybag" };
                                        var item01 = level3Items[rdm.Next(level3Items.Length)];
                                        var item02 = level3Items[rdm.Next(level3Items.Length)];
                                        x.Text += $"You got 5000 coins and: {item01} {DBFunctions.GetItemEmote(item01)}, {item02} {DBFunctions.GetItemEmote(item02)}";
                                        await u.GiveCoinsAsync(5000);
                                        u.GiveItem(item01);
                                        u.GiveItem(item02);
                                        break;
                                    case 4:
                                        string[] level4Items = { "key2", "gun", "unicorn", "moneybag", "moneybag", "gem", "unicorn" };
                                        var item1 = level4Items[rdm.Next(level4Items.Length)];
                                        var item2 = level4Items[rdm.Next(level4Items.Length)];
                                        var item3 = level4Items[rdm.Next(level4Items.Length)];
                                        var item4 = level4Items[rdm.Next(level4Items.Length)];
                                        x.Text += $"You got 10000 coins and: {item1} {DBFunctions.GetItemEmote(item1)}, {item2} {DBFunctions.GetItemEmote(item2)}, {item3} {DBFunctions.GetItemEmote(item3)}, {item4} {DBFunctions.GetItemEmote(item4)}";
                                        await u.GiveCoinsAsync(10000);
                                        u.GiveItem(item1);
                                        u.GiveItem(item2);
                                        u.GiveItem(item3);
                                        u.GiveItem(item4);
                                        DBFunctions.AddNews($"{Context.User.Username.ToUpper()} WINS THE LOTTERY!", $"{Context.User.Username} is taking home the big bucks, managing to score a whopping 10,000 coins along with a " +
                                            $"{item1}, {item2}, {item3}, and a {item4}. All these thanks to their lucky number {Var.todaysLotto}! Those four numbers are what currently separates the big leagues from us " +
                                            $"puny mortals! {Var.CurrentDateFormatted()} {Context.User.Username} checked to see their match and was met with a lifechanging surprise! Congratulations, {Context.User.Username} and " +
                                            $"who knows? Maybe YOU could be next! Use `;lottery buy` to get your ticket today and get your chance to live the dream! THIS MESSAGE WAS BROUGHT TO YOU BY LOTTO CO™️");
                                        break;
                                }
                            }
                        }));
                    }
                    emb.Footer.Text = $"Your number: {uNum}";
                }


                await ReplyAsync("", embed: emb.Build());
            }
            else if (command == "buy")
            {
                string uNum = u.GetData<string>("lotto_num");
                if (uNum == "0") await ReplyAsync("Are you sure you want to buy a lottery ticket for 10 coins? Use `;lottery confirm` to confirm!");
                else await ReplyAsync("Are you sure you want to buy a *new* lottery ticket for 100 coins? Use `;lottery confirm` to confirm!");
            }
            else if (command == "confirm")
            {
                string uNum = u.GetData<string>("lotto_num");
                int cost = 0;
                if (uNum == "0") cost = 10;
                else cost = 100;

                if (u.GetCoins() >= cost)
                {
                    await u.GiveCoinsAsync(-cost);
                    string lottonum = $"{rdm.Next(10)}{rdm.Next(10)}{rdm.Next(10)}{rdm.Next(10)}";
                    u.SetData("lotto_num", lottonum);
                    await ReplyAsync($"You have successfully purchased a Happy Lucky Lottery Ticket for {cost} coins! Your number is: " + lottonum);
                }
                else
                {
                    await ReplyAsync($"You cannot afford a ticket! You need {cost} coins.");
                }
            }
        }

        [Command("tip"), Summary("[FUN] Get a random ForkBot tip, or use a number as the parameter to get a specific tip!")]
        public async Task Tip(int tipNumber = -1)
        {
            string[] tips = { "Use `;makekey` to combine 5 packages into a key!",
                "Get presents occasionally with `;present`! No presents left? Use a ticket to add more to the batch, or a stopwatch to shorten the time until the next batch!",
                "Get coins for items you don't need or want by selling them with `;sell`! Item can't be sold? Just `;trash` it!",
                "Give other users coins with the `;donate` command!",
                "Legend says of a secret shop that only the most elite may enter! I think the **man** knows...",
                "Did you know that you can sell ALL of your items using ;sell all? Be careful! You will lose EVERYTHING!",
                "Check professor ratings using the ;prof command!"
                };
            if (tipNumber == -1) tipNumber = rdm.Next(tips.Length);
            else tipNumber--;

            if (tipNumber < 0 || tipNumber > tips.Length) await ReplyAsync($"Invalid tip number! Make sure number is above 0 and less than {tips.Length + 1}");
            await ReplyAsync($":robot::speech_balloon: " + tips[tipNumber]);
        }
        /*
        [Command("minesweeper"), Summary("[FUN] Play a game of MineSweeper and earn coins!"), Alias(new string[] { "ms" })]
        public async Task MineSweeper([Remainder]string command = "")
        {

            MineSweeper game = Var.MSGames.Where(x => x.player.ID == Context.User.Id).FirstOrDefault();
            if (game == null)
            {
                game = new MineSweeper(User.Get(Context.User));
                await ReplyAsync(game.Build());
                Var.MSGames.Add(game);
                await ReplyAsync("Use `;ms x y` (replacing x and y with letter coordinates) to reveal a tile, or `;ms flag x y` to flag a tile.");
            }
            else if (command.ToLower().StartsWith("flag") && game != null)
            {
                var coords = command.ToLower().Replace(" ", "").ToCharArray();
                var success = game.Flag(coords);
                if (success) await ReplyAsync(game.Build());
                else await ReplyAsync("Make sure the tile you choose is unrevealed.");
            }
            else
            {
                var coords = command.ToLower().Replace(" ", "").ToCharArray();
                var success = game.Turn(coords);
                if (success) await ReplyAsync(game.Build());
                else await ReplyAsync("Make sure the tile you choose is unrevealed.");
            }
        }*/

        [Command("talk"), Summary("Chat time with ForkBot.")]
        public async Task Talk([Remainder] string input = "")
        {
            if (input.ToLower().StartsWith("remember") && Context.User.Id == Constants.Users.BRADY)
            {


                Dictionary<string, string> replacements = new Dictionary<string, string>
                {
                    { "remember","" },
                    { "you're", "Stevey is" },
                    { "yours", "Steveys" },
                    { "your", "Steveys" },
                    { "you", "Stevey" },
                    { "i", Context.User.Username },
                    { "me", Context.User.Username },
                    { "my", Context.User.Username + 's' },
                    { "mine", Context.User.Username + 's'},
                    { "were", "was" },
                    { "we're", $"Stevey and {Context.User.Username} are" },
                    { "we've", $"Stevey and {Context.User.Username} have" },
                    { "we", $"Stevey and {Context.User.Username}" }

                };

                int wordCount = Regex.Matches(input, "\\w+|[,.!?]").Count();

                var user = User.Get(Context.User.Id);
                int usedWords = user.GetData<int>("GPTWordsUsed");

                int userTokenCount = (int)(usedWords+wordCount * 1.4);

                if (!user.HasItem("keyboard") && userTokenCount > Stevebot.Chat.MAX_USER_TOKENS)
                {
                    await ReplyAsync($"Sorry, you've used up your monthly tokens of {Stevebot.Chat.MAX_USER_TOKENS}. Donate at https://www.paypal.me/Brady0423 and get this limit removed.");
                    return;
                }


                input = input.ToLower();

                foreach (var replace in replacements)
                {
                    input = Regex.Replace(input, $"([^a-z]|^)({replace.Key})([^a-z]|$)", $"$1{replace.Value}$3");
                }

                input = input.Trim(' ', '.', '?', '!', ',');

                var memory = DBFunctions.GetProperty("chat_memory").ToString();

                DBFunctions.SetProperty("chat_memory",memory + input + ". ");

                await ReplyAsync("Okay, I'll remember that.");

            }
            else if (Stevebot.Chat.Chats.Where(x => x.channel_id == Context.Channel.Id).Count() > 0)
            {
                if (input.ToLower() == "end") Stevebot.Chat.Chats.Remove(Stevebot.Chat.Chats.Where(x => x.channel_id == Context.Channel.Id).First());
                else await Context.Channel.SendMessageAsync("We're already chatting here.");
            }
            else
            {
                await Context.Message.AddReactionAsync(Emoji.Parse("💬"));
                Stevebot.Chat newChat = null;
                if (input == "" || input == " ")
                {
                    var request = new CompletionCreateRequest()
                    {
                        Prompt = "Say a greeting for a conversation:\n",
                        MaxTokens = 128,
                        Temperature = 0.8f
                    };

                    var completion = await Stevebot.Chat.OpenAI.Completions.CreateCompletion(request, OpenAI.GPT3.ObjectModels.Models.Davinci);
                    string firstMsg = completion.Choices.First().Text;



                    var trimmed = firstMsg.Trim('"', ' ', '"', '\n');
                    await Context.Channel.SendMessageAsync(trimmed);
                    newChat = new Stevebot.Chat(Context.User.Id, Context.Channel.Id, trimmed);
                }
                else
                {
                    newChat = new Stevebot.Chat(Context.User.Id, Context.Channel.Id);
                    await Context.Channel.SendMessageAsync(await newChat.GetNextMessageAsync(Context.Message));
                }
                await Context.Message.RemoveReactionAsync(Emoji.Parse("💬"), Bot.client.CurrentUser);
            }
        }

        [Command("gpt")]
        public async Task GPT([Remainder] string input)
        {
            var user = User.Get(Context.User.Id);
            int usedWords = user.GetData<int>("GPTWordsUsed");

            if (input.ToLower() == "usage")
            {
                if (user.HasItem("keyboard"))
                    await ReplyAsync($"All hail thy who possesses the almighty board of keys. Your tongue is free.\n💵 You've used {(int)(usedWords * 1.4)} / {Stevebot.Chat.MAX_USER_TOKENS} tokens.]");
                else
                    await ReplyAsync($"💵 You have used: {(int)(usedWords * 1.4)} / {Stevebot.Chat.MAX_USER_TOKENS} tokens.");
                return;
            }

            int wordCount = Regex.Matches(input, "\\w+|[,.!?]").Count();
            int userTokenCount = (int)((usedWords + wordCount) * 1.4);

            if (!user.HasItem("keyboard") && userTokenCount > Stevebot.Chat.MAX_USER_TOKENS)
            {
                await ReplyAsync($"Sorry, you've used up your monthly limit of {Stevebot.Chat.MAX_USER_TOKENS} tokens. Donate at https://www.paypal.me/Brady0423 and get this limit removed.");
                return;
            }

            await Context.Message.AddReactionAsync(Constants.Emotes.SPEECH_BUBBLE);

            try
            {
                var request = new CompletionCreateRequest()
                {
                    Prompt = input,
                    MaxTokens = Math.Min(Stevebot.Chat.MAX_USER_TOKENS - userTokenCount, 256),
                    Temperature = 0.7f
                };

                var completion = await Stevebot.Chat.OpenAI.Completions.CreateCompletion(request, OpenAI.GPT3.ObjectModels.Models.Davinci);
                string resp = completion.Choices.First().Text;

                wordCount += Regex.Matches(resp.ToString(), "\\w+|[,.!?]").Count();
                user.AddData("GPTWordsUsed", wordCount);

                string response = resp.ToString().Replace("@everyone", $"[{Context.User.Username} smells like stale ass]");
                response = Regex.Replace(response, "(<@([0-9]*)>)", x =>
                {
                    ulong id = 0;
                    if (ulong.TryParse(x.Groups[2].Value, out id))
                    {
                        return Context.Guild.GetUserAsync(id).Result.Username; // i know this should be awaited dont tell anyone
                    }
                    else return "";
                });

                response = Regex.Replace(response, "(<@&([0-9]*)>)", x =>
                {
                    ulong id = 0;
                    if (ulong.TryParse(x.Groups[2].Value, out id))
                    {
                        return Context.Guild.GetRole(id).Name;
                    }
                    else return "";
                });

                response = response.Replace("@", "");

                await Context.Message.ReplyAsync(response);
                await Context.Message.RemoveReactionAsync(Constants.Emotes.SPEECH_BUBBLE, Constants.Users.FORKBOT);
            }
            catch (HttpRequestException)
            {
                await ReplyAsync("Sorry! Seems we're out of credits. If you'd like to personally donate for more, you can use this link: https://www.paypal.me/Brady0423");
            }
        }

        /*
        [Command("forkparty"), Summary("[FUN] Begin a game of ForkParty:tm: with up to 4 players!"), Alias(new string[] { "fp" })]
        public async Task ForkParty([Remainder] string command = "")
        {
            if (await Functions.isDM(Context.Message))
            {
                await ReplyAsync("Sorry, this command cannot be used in private messages.");
                return;
            }
            var user = User.Get(Context.User);
            var chanGames = Var.FPGames.Where(x => x.Channel.Id == Context.Channel.Id);
            ForkParty game = null;
            if (chanGames.Count() != 0) game = chanGames.First();
            if (command == "")
            {
                string msg = "Welcome to ForkParty:tm:! This is a Mario Party styled game in which players move around a board and play minigames to collect the most Forks and win!\n" +
                                "Use `;fp host` to start a game, or `;fp join` in the hosts channel to join a game.";

                if (game != null)
                {
                    msg += $"\n```\nThere is currently a game being hosted by {await game.Players[0].GetName(Context.Guild)}. There is {4 - game.PlayerCount} spot(s) left.\n";
                    for (int i = 1; i < game.PlayerCount; i++) msg += await game.Players[i].GetName(Context.Guild) + "\n";
                    for (int i = 0; i < 4 - game.PlayerCount; i++) msg += "----------\n";
                    msg += "```";
                }

                await ReplyAsync(msg);
            }
            else if (command == "host")
            {
                if (game != null)
                {
                    string msg = "There is already a ForkParty game being hosted in this channel.";
                    if (game.Started) msg += " It has not started yet, join with `;fp join`!";
                    await ReplyAsync(msg);
                }
                else
                {
                    Var.FPGames.Add(new ForkParty(user, Context.Channel));
                    await ReplyAsync("You have successfully hosted a game. Get others to join now!");
                }

            }
            else if (command == "join")
            {
                if (game != null)
                {
                    if (!game.Started)
                    {
                        if (game.PlayerCount < 4)
                        {
                            if (!game.HasPlayer(user))
                            {
                                game.Join(user);
                                await ReplyAsync($"You have successfully joined {await game.Players[0].GetName(Context.Guild)}'s game.");
                            }
                            else
                                await ReplyAsync("You have already joined this game.");
                        }
                        else await ReplyAsync("There are no spaces remaining in the game being hosted in this channel.");
                    }
                    else await ReplyAsync("There is already game started in this channel. Wait for it to end or find another!");
                }
                else await ReplyAsync("There is currently no game being hosted in this channel. Host a game with `;fp host`!");
            }
        }



        /*[Command("pokemon"), Summary("[FUN] Use various pokemon commands."), Alias("pm")]
        public async Task Pokemon(params  string[] command)
        {
            if (command.Length == 0) command = new string[] { "" };

            switch (command[0])
            {
                case "trade"
            }

        }*/
        #endregion

        #region Mod Commands

        [Command("ban"), RequireUserPermission(GuildPermission.BanMembers), Summary("[MOD] Bans the specified user.")]
        public async Task Ban(IGuildUser u, int minutes = 0, [Remainder]string reason = null)
        {
            string rText = ".";
            if (reason != null) rText = $" for: \"{reason}\".";
            InfoEmbed banEmb = new("USER BAN", $"User: {u} has been banned{rText}.", Constants.Images.Ban);
            await Context.Guild.AddBanAsync(u, reason: reason);
            await Context.Channel.SendMessageAsync("", embed: banEmb.Build());
        }

        [Command("kick"), RequireUserPermission(GuildPermission.KickMembers), Summary("[MOD] Kicks the specified user.")]
        public async Task Kick(IUser u, [Remainder]string reason = null)
        {
            string rText = ".";
            if (reason != null) rText = $" for: \"{reason}\".";
            InfoEmbed kickEmb = new("USER KICK", $"User: {u} has been kicked{rText}", Constants.Images.Kick);
            await (u as IGuildUser).KickAsync(reason);
            await Context.Channel.SendMessageAsync("", embed: kickEmb.Build());
        }

        [Command("purge"), RequireUserPermission(GuildPermission.ManageMessages), Summary("[MOD] Delete [amount] messages")]
        public async Task Purge(int amount)
        {
            Var.purging = true;
            var messages = await Context.Channel.GetMessagesAsync(amount + 1).FlattenAsync();
            await (Context.Channel as ITextChannel).DeleteMessagesAsync(messages);

            InfoEmbed ie = new("PURGE", $"{amount} messages deleted by {Context.User.Username}.");
            Var.purgeMessage = await Context.Channel.SendMessageAsync("", embed: ie.Build());
            Timers.unpurge = new Timer(new TimerCallback(Timers.UnPurge), null, 5000, Timeout.Infinite);
        }

        [Command("block"), Summary("[MOD] Temporarily stops users from being able to use the bot.")]
        public async Task Block(IUser u)
        {
            if (Context.User.Id != Constants.Users.BRADY) { await ReplyAsync("This can only be used by the bot owner."); return; }
            if (Var.blockedUsers.Contains(u))
            {
                Var.blockedUsers.Remove(u);
                await ReplyAsync("Unblocked.");
            }
            else
            {
                Var.blockedUsers.Add(u);
                await ReplyAsync("Blocked.");
            }

        }

        [Command("block")]
        public async Task Block(ulong id)
        {
            if (Context.User.Id != Constants.Users.BRADY) { await ReplyAsync("This can only be used by the bot owner."); return; }
            var u = await Bot.client.GetUserAsync(id);
            if (Var.blockedUsers.Contains(u))
            {
                Var.blockedUsers.Remove(u);
                await ReplyAsync("Unblocked.");
            }
            else
            {
                Var.blockedUsers.Add(u);
                await ReplyAsync("Blocked.");
            }

        }

        /*
        [Command("blockword"), RequireUserPermission(GuildPermission.ManageMessages), Summary("[MOD] Adds the inputted word to the word filter.")]
        public async Task BlockWord([Remainder] string word)
        {
            if (Context.Guild.Id != Constants.Guilds.YORK_UNIVERSITY) { await ReplyAsync("This can only be used in the York University server."); return; }
            Properties.Settings.Default.blockedWords += word + "|";
            Properties.Settings.Default.Save();
            await ReplyAsync("", embed: new InfoEmbed("Word Blocked", "Word successfully added to filter.").Build());
            await Context.Message.DeleteAsync();
        }
        */
        
        [Command("lockdown"), Summary("[MOD] Locks the server")]
        public async Task Lockdown()
        {
            if (Context.Guild.Id != Constants.Guilds.YORK_UNIVERSITY) return;
            var gUser = Context.User as IGuildUser;
            if (!gUser.GuildPermissions.Administrator) return;
            Var.LockDown = !Var.LockDown;
            if (Var.LockDown) await ReplyAsync("Server locked.");
            else await ReplyAsync("Server unlocked.");
        }

        [Command("fban"), RequireUserPermission(GuildPermission.KickMembers), Summary("[MOD] Pretend to ban someone hahahahaha..")]
        public async Task FBan(string user)
        {
            await Context.Message.DeleteAsync();
            await ReplyAsync($"{user} has left the server.");
        }

        [Command("fban"), RequireUserPermission(GuildPermission.KickMembers), Summary("[MOD] Pretend to ban someone hahahahaha..")]
        public async Task FBan(IUser user) => await FBan(user.Username);

        /* archived
        [Command("unbanall"), RequireUserPermission(GuildPermission.BanMembers), Summary("[MOD] Unban everyone.")]
        public async Task UnbanAll()
        {
            var bans = await Context.Guild.GetBansAsync().FirstAsync();
            foreach(var ban in bans)
            {
                var u = ban.User;
                await Context.Guild.RemoveBanAsync(u);
            }
        }
        */

        #endregion

        #region Brady Commands

        [Command("todo"), Summary("[BRADY] View/add reminders or remove them by prefixing with '-'.")]
        public async Task Todo([Remainder] string reminder = "")
        {
            if (reminder != "")
            {
                if (Context.User.Id == Constants.Users.BRADY)
                {
                    if (reminder.Trim().StartsWith("-"))
                    {
                        bool removed = false;
                        List<string> reminders = File.ReadAllLines("Files/reminders.txt").ToList();
                        for (int i = 0; i < reminders.Count; i++)
                        {
                            if (reminders[i].ToLower().StartsWith(reminder.Trim().Replace("-", "").Trim().ToLower()))
                            {
                                reminders.RemoveAt(i);
                                File.WriteAllLines("Files/reminders.txt", reminders);
                                removed = true;
                                break;
                            }
                        }
                        if (removed) await Context.Channel.SendMessageAsync("Successfully removed reminder.");
                        else await Context.Channel.SendMessageAsync("Specified reminder not found.");
                    }
                    else
                    {
                        File.AppendAllText("Files/reminders.txt", reminder + "\n");
                        await Context.Channel.SendMessageAsync("Added");
                    }
                }
            }
            else
            {
                string reminders = File.ReadAllText("Files/reminders.txt");
                foreach (string s in Functions.SplitMessage(reminders))
                {
                    await Context.Channel.SendMessageAsync(s);
                }
            }
        }

        [Command("givecoins"), Summary("[BRADY] Give a user [amount] coins.")]
        public async Task Give(IUser user, int amount)
        {
            if (Context.User.Id == Constants.Users.BRADY)
            {
                User u = User.Get(user);
                await u.GiveCoinsAsync(amount);
                await Context.Channel.SendMessageAsync($"{user.Username} has successfully been given {amount} coins.");
            }
            else await Context.Channel.SendMessageAsync("Sorry, only Brady can use this right now.");
        }

        [Command("giveitem"), Summary("[BRADY] Give a user [item] item")]
        public async Task Give(IUser user, string item)
        {
            if (Context.User.Id == Constants.Users.BRADY)
            {
                User u = User.Get(user);
                var success = u.GiveItem(item);
                if (success) await Context.Channel.SendMessageAsync($"{user.Username} has successfully been given: {item}.");
                else await ReplyAsync($"Failed to give item. Are you sure '{item}' exists?");
            }
            else await Context.Channel.SendMessageAsync("Sorry, only Brady can use this right now.");
        }

        [Command("respond"), Summary("[BRADY] Toggles whether the bot should listen or respond to messages with AI")]
        public async Task Respond()
        {
            if (Context.User.Id == Constants.Users.BRADY)
            {
                Var.responding = !Var.responding;
                if (Var.responding) await Context.Channel.SendMessageAsync("Responding");
                else await Context.Channel.SendMessageAsync("Listening");
            }
        }

        [Command("sblock"), Summary("[BRADY] Blocks specified [user] from giving suggestions.")]
        public async Task SuggestionBlock(IUser user)
        {
            if (Context.User.Id != Constants.Users.BRADY) throw NotBradyException;
            User.Get(Context.User).SetData("Suggest_Blocked", 1);
            await Context.Channel.SendMessageAsync("Blocked");
        }

        [Command("addcourse"), Summary("[BRADY] Adds the specified course to the course list.")]
        public async Task AddCourse([Remainder]string course = "")
        {
            if (Context.User.Id != Constants.Users.BRADY) throw NotBradyException;
            if (course != "")
            {
                course.Replace("\\t", "\t");
                File.AppendAllText("Files/courselist.txt", "\n" + course);
                await ReplyAsync("Successfully added course.");
            }
            else await ReplyAsync("FORMAT EXAMPLE: `LE/EECS 4404 3.00\tIntroduction to Machine Learning and Pattern Recognition`");
        }

        [Command("giveallitem"), Summary("[BRADY] Give all users an item and optionally display a message.")]
        public async Task GiveAllItem(string item, [Remainder] string msg = "")
        {
            var users = Directory.GetFiles("Users");
            foreach (string u in users)
            {
                var uID = u.Replace(".user", "").Split('\\')[1];
                try
                {
                    var user = await Bot.client.GetUserAsync(Convert.ToUInt64(uID));
                    User.Get(user).GiveItem(item);
                }
                catch (Exception) { Console.WriteLine($"Unable to give user ({u}) item."); }
            }
            if (msg != "") await ReplyAsync("", embed: new InfoEmbed("", msg += $"\nEveryone has received a(n) {item}!", Constants.Images.ForkBot).Build());
        }

        [Command("courses")]
        public async Task Courses() { await Courses("", ""); }

        [Command("courses"), Summary("[BRADY] Return all courses with certain parameters. Leave parameters blank for examples. THIS COMMAND MAY TAKE A LONG TIME.")]
        public async Task Courses(string subject, [Remainder] string commands = "")
        {
            if (Context.User.Id != Constants.Users.BRADY)
            {
                await ReplyAsync("Sorry, only Brady can use this right now. I'm testing it.");
                return;
            }

            if (subject == "" && commands == "")
            {
                string msg = "This command generates a list of courses that fall under the chosen parameters in order to help find courses with certain criteria easier.\n\n" +
                             "`;courses EECS day:MTWR term:W` Shows EECS courses with classes from Monday to Thursday (Thursday is an R to reflect York website) during the **W**inter term.\n\n" +
                             "`;courses ITEC time>16 day:MWF` Shows ITEC courses with classes after 4pm (24 hour clock) and on Monday, Wednesday, or Friday.\n\n" +
                             "Time can be structed as `time>#`, or 'time<#' for classes after a certain time and classes before a certain time.\n\n" +
                             "`;courses MATH credit:3 level:3???` Shows MATH courses with 3 credits in the 3000 level. For levels, ?'s are wildcards and can be replaced with any number.\n\n" +
                             "Parameters dont need to be in any specific order, just seperated by spaces. However, the subject (MATH, EECS, ITEC, etc) must be first.";
                await ReplyAsync("", embed: new InfoEmbed(";courses EXAMPLES", msg).Build());
                return;
            }

            if (commands.Split(' ').Length > 4)
            {
                await ReplyAsync("Over max parameter count. Are you sure all parameters are correct and you are not using the same one multiple times?");
                return;
            }

            await ReplyAsync("Filtering... Please wait...");

            commands = commands.Replace("time>", "time>:").Replace("time<", "time<:").ToLower();

            Dictionary<string, string> parameters = new ();
            parameters.Add("department", subject);
            foreach (string command in commands.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!command.Contains(':') && command.Length > 1) await ReplyAsync($"Parameter '{command}' invalid. Continuing without this parameter.");
                else if (!command.Contains(':'))
                {
                    await ReplyAsync($"Parameter '{command}' invalid. Cancelling.");
                    return;
                }
                else
                {
                    string[] cParams = command.Split(':');
                    var type = cParams[0];
                    var cond = cParams[1];
                    parameters.Add(type, cond);
                }
            }

            var courses = YorkU.Course.GetCourses(parameters);

            string text = "";
            foreach(Course c in courses)
            {
                text += $"{c.Coursecode} - {c.Title}\n";
            }


            JEmbed emb = new()
            {
                Title = $"Filtered course list: [{subject} {commands}]",
                Description = text,
                ColorStripe = Constants.Colours.YORK_RED
            };
            emb.Author.IconUrl = Constants.Images.ForkBot;
            emb.Footer.Text = "Remember, this list may not be accurate as courses may no longer be running or new courses may be added that are not on the list.";
            await ReplyAsync("", embed: emb.Build());


        }

        [Command("debugmode"), Summary("[BRADY] Set bot to debug mode, disables other users and enables some other features.")]
        public async Task DebugMode(int code)
        {
            if (code == Var.DebugCode && Context.User.Id == Constants.Users.BRADY)
            {
                Var.DebugMode = !Var.DebugMode;
                Console.WriteLine("DebugMode set to " + Var.DebugMode);
                await (Context.Channel.SendMessageAsync(Var.DebugMode.ToString()));
            }
        }

        [Command("givedebug"), Summary("[BRADY] Allows or blocks the specified user from being able to use commands while in debug mode.")]
        public async Task GiveDebug(IUser user)
        {
            if (Context.User.Id == Constants.Users.BRADY)
            {
                if (Var.DebugUsers.Where(x => x.Id == user.Id).Count() > 0)
                {
                    Var.DebugUsers = Var.DebugUsers.Where(x => x.Id != user.Id).ToList();
                    await ReplyAsync("Removed.");
                }
                else
                {
                    Var.DebugUsers.Add(user);
                    await ReplyAsync("Added.");
                }

            }
        }

        /*
        [Command("transfer"), Summary("[BRADY] Transfers all account data from one user to another.")]
        public async Task Transfer(IUser oldUser, IUser newUser)
        {
            if (Context.User.Id != Constants.Users.BRADY) return;
            var user1 = User.Get(oldUser);
            var user2 = User.Get(newUser);
            string oldData = user1.GetFileString();
            user2.Archive(true);
            user2.SetFileString(oldData);
            user1.Archive();
            await ReplyAsync("Successfully transfered data and archived old user.");
        }
        */

        [Command("makebid"), Summary("[BRADY] Creates a new bid starting at 100 coins.")]
        public async Task MakeBid(string item = "", int amount = 0)
        {
            if (Context.User.Id != Constants.Users.BRADY) return;

            if (item == "" || amount <= 0)
            {
                await ReplyAsync("Holy shit, can you stop forgetting the fucking parameters? Idiot. It's `;makebid [item] [amount]`.\n" +
                    "DON'T FUCKING FORGET IT.");
                return;
            }

            var newBid = new Bid(DBFunctions.GetItemID(item), amount);

            var notifyUserIDs = DBFunctions.GetUserIDsWhere("Notify_Bid", "1");
            foreach (ulong id in notifyUserIDs)
            {
                var u = await Bot.client.GetUserAsync(id);
                await u.SendMessageAsync("", embed: new InfoEmbed("New Bid Alert", $"There is a new bid for {amount} {item}(s)! Get it with the ID: {newBid.ID}.\n*You are receiving this message because you have opted in to new bid notifications.*").Build());
            }
        }

        [Command("lockdm"), Summary("[BRADY] Locks command usage via DM.")]
        public async Task LockDM()
        {
            if (Context.User.Id != Constants.Users.BRADY) return;
            Var.LockDM = !Var.LockDM;

            if (Var.LockDM) await ReplyAsync("DM Commands have been disabled.");
            else await ReplyAsync("DM Commands have been enabled.");
        }

        [Command("testsql"), Summary("[BRADY] Run an SQL query.")]
        public async Task TestSQL([Remainder]string query)
        {
            if (Context.User.Id != Constants.Users.BRADY) return;


            var con = new SQLiteConnection(Constants.Values.DB_CONNECTION_STRING);
            con.Open();
            var cmd = new SQLiteCommand(query, con);

            var returnData = cmd.ExecuteReader();
            string header = "";
            bool headerSet = false;
            string msg = "";
            while (returnData.Read())
            {
                for (int i = 0; i < returnData.FieldCount; i++)
                {
                    if (!headerSet) header += $"{returnData.GetName(i)}\t|\t";
                    msg += $"{returnData.GetValue(i)}\t|\t";
                }
                headerSet = true;
                msg += "\n";
            }
            await ReplyAsync(header + "\n" + msg);
            //await ReplyAsync(version);
        }

        [Command("makenews"), Summary("[BRADY] Create a news article and add it to the newspaper.")]
        public async Task MakeNews(string title, string content)
        {
            DBFunctions.AddNews(title, content);
            await ReplyAsync("Done.");
        }

        /*
        [Command("snap")]
        public async Task Snap()
        {
            if (Context.User.Id != Constants.Users.BRADY) return;
            await Context.Channel.SendMessageAsync("When I’m done, half of humanity will still exist. Perfectly balanced, as all things should be.\n\nI know what it’s like to lose. To feel so desperately that you’re right, yet to fail nonetheless. Dread it. Run from it. Destiny still arrives. Or should I say, I have.");

            var users = await Context.Guild.GetUsersAsync();
            List<int> dustIndex = new List<int>();
            int userCount = users.Count();
            int halfUsers = userCount / 2;
            for (int i = 0; i < halfUsers; i++)
            {
                try
                {
                    int index = -1;
                    while (index < 0 || dustIndex.Contains(index))
                        index = rdm.Next(userCount);

                    dustIndex.Add(index);
                    await users.ElementAt(index).AddRoleAsync(Context.Guild.GetRole(Constants.Roles.DUST));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"{e.StackTrace}\n\n on user index {i} ({users.ElementAt(i).Username})");
                }
            }
        }
        */


        #endregion

    }
    
}
