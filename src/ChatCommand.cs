using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickSort
{
    public abstract class Argument
    {
        public string name;
    }

    public class Argument<T> : Argument
    {
        public T value;

        public Argument(string name, T value)
        {
            base.name = name;
            this.value = value;
        }

        public static implicit operator T(Argument<T> arg)
        {
            return arg.value;
        }
    }

    public struct ChatArgs
    {
        public Argument[] arguments;
        public string help;
        public int Length => arguments.Length;
        public bool Empty => arguments.Length == 0;
        public bool this[string name] => Get<bool>(name);
        public string this[int name] => Get<string>(name);

        public T Get<T>(string name)
        {
            Argument[] array = arguments;
            foreach (Argument argument in array)
            {
                if (argument.name == name)
                {
                    return ((Argument<T>)argument).value;
                }
            }
            return default(T);
        }

        public T Get<T>(int name)
        {
            Argument[] array = arguments;
            foreach (Argument argument in array)
            {
                if (argument.name == name.ToString())
                {
                    return ((Argument<T>)argument).value;
                }
            }
            return default(T);
        }
    }

    public class ChatCommand
    {
        public static List<ChatCommand> Commands = new List<ChatCommand>();

        public string keyword;
        public Action<ChatArgs> action;
        public string help;

        public ChatCommand(string keyword, string help, Action<ChatArgs> action)
        {
            this.keyword = keyword;
            this.help = help;
            this.action = action;
        }

        public static ChatCommand New(string keyword, string help, Action<ChatArgs> action)
        {
            ChatCommand chatCommand = new ChatCommand(keyword, help, action);
            Commands.Add(chatCommand);
            return chatCommand;
        }

        public ChatArgs GetArgs(string raw)
        {
            List<Argument> list = new List<Argument>();
            string[] array = raw.Split(' ');

            keyword = array[0];

            for (int i = 1; i < array.Length; i++)
            {
                if (array[i].StartsWith("-"))
                {
                    string name = array[i].Substring(1);
                    list.Add(new Argument<bool>(name, value: true));
                }
                else
                {
                    list.Add(new Argument<string>((i - 1).ToString(), array[i]));
                }
            }

            ChatArgs result = default(ChatArgs);
            result.arguments = list.ToArray();
            result.help = help;
            return result;
        }

        public override string ToString()
        {
            return keyword;
        }
    }
}

