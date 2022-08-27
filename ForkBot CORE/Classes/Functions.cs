using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Net;
using System.Xml;
using System.Text.RegularExpressions;
using System.Data.SQLite;

namespace ForkBot
{
    public class Functions
    {
        public static Color GetColor(IUser User)
        {
            var user = User as IGuildUser;
            if (user != null)
            {
                if (user.RoleIds.ToArray().Length > 1)
                {
                    var role = user.Guild.GetRole(user.RoleIds.ElementAtOrDefault(1));
                    return role.Color;
                }
                else return Constants.Colours.DEFAULT_COLOUR;
            }
            else return Constants.Colours.DEFAULT_COLOUR;
        }

        

        //returns a users nickname if they have one, otherwise returns their username.
        public static string GetName(IGuildUser user)
        {
            if (user.Nickname == null)
                return user.Username;
            return user.Nickname;
        }

        char[] vowels = {'a','e','i','o','u'};
        public static string GetPrefix(string word)
        {
            if (vowels.Contains(word[0]))
                return "An";
            else
                return "A";
        }
        
        public static ItemTrade? GetTrade(IUser user)
        {
            foreach (ItemTrade trade in Var.trades)
            {
                if (trade.HasUser(user))
                {
                    return trade;
                }
            }
            return null;
        }
                
        //splits a message into multiple message when its too long (over 2000 chars)
        public static string[] SplitMessage(string msg)
        {
            List<string> msgs = new List<string>();
            int start = 0;
            for(; msg.Length !=start; )
            {
                //find good spot to split
                int splitIndex = -1;
                if (msg.Length - start < 2000)
                {
                    splitIndex = msg.Length - 1;
                }
                else
                {
                    for (int j = start + 1900; j < start + 1999; j++)
                    {
                        if (msg[j] == '\n')
                        {
                            splitIndex = j;
                            break;
                        }
                    }
                    if (splitIndex == -1)
                        for (int j = start + 1900; j < start + 1999; j++)
                        {
                            if (msg[j] == ' ')
                            {
                                splitIndex = j;
                                break;
                            }
                        }
                    if (splitIndex == -1) splitIndex = start + 1999;
                }
                int end = splitIndex - start;
                msgs.Add(msg.Substring(start, end));
                start = splitIndex + 1;
            }
            return msgs.ToArray();
            
        }
 
        public static async Task<bool> isDM(IMessage message)
        {
            return message.Channel is IPrivateChannel;
        }
    }


    static class Func
    {
        public static string ToTitleCase(this string s)
        {
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLower());
        }
    }
}
