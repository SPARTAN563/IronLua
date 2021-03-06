using System;
using System.IO;
using IronLua.Runtime;
using System.Collections.Generic;

namespace IronLua.Library
{
    class OSLibrary : Library
    {
        public OSLibrary(CodeContext context)
            : base(context)
        {
        }

        public static readonly DateTime Epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static double Time(LuaTable t)
        {
            //if (t == null)
            //{
                return (DateTime.UtcNow - Epoch).TotalSeconds;
            //}

            //throw new NotImplementedException();
        }

        public static object Delete(string fn)
        {
            try
            {
                File.Delete(fn);
                return true;
            }
            catch (Exception ex)
            {
                return new Varargs(null, ex.Message, ex);
            }            
        }

        public static object Rename(string of, string nf)
        {
            try
            {
                File.Move(of, nf);
                return true;
            }
            catch (Exception ex)
            {
                return new Varargs(null, ex.Message, ex);                
            }
        }

        public override void Setup(IDictionary<string, object> table)
        {
            table.AddOrSet("time", (Func<LuaTable, double>)Time );
            table.AddOrSet("difftime", (Func<double, double, double>) ((t2, t1) => t2 - t1));

            //table.AddOrSet("date", (Func<object, object>)Date); // TODO

            table.AddOrSet("exit", (Action<double>)(e => Environment.Exit((int)e)));
            table.AddOrSet("getenv", (Func<string, string>) Environment.GetEnvironmentVariable);

            table.AddOrSet("remove", (Func<string, object>) Delete);
            table.AddOrSet("rename", (Func<string, string, object>) Rename);
        }
    }
}