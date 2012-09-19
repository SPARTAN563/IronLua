﻿using System;
using System.Runtime.Serialization;
using IronLua.Runtime;
using Microsoft.Scripting;
using System.Collections;
using System.Collections.Generic;
using IronLua.Library;
using System.Linq;

namespace IronLua
{
    [Serializable]
    public class LuaRuntimeException : LuaException
    {
        internal LuaRuntimeException(CodeContext context, string message = null, Exception inner = null)
            : base(message, inner)
        {
            Context = context;
            stack = new Stack<FunctionStack>(context.FunctionStacks.Reverse());
        }

        internal LuaRuntimeException(CodeContext context, string format, params object[] args)
            : base(String.Format(format, args))
        {
            Context = context;
            stack = new Stack<FunctionStack>(context.FunctionStacks.Reverse());
        }
        
        protected LuaRuntimeException(SerializationInfo info, StreamingContext context) 
            : base(info, context)
        {
        }

        /// <summary>
        /// Gets the current execution context in which the error occured
        /// </summary>
        internal CodeContext Context
        { get; private set; }

        ///// <summary>
        ///// Gets the currently executing block of code
        ///// </summary>
        //public SourceSpan CurrentBlock
        //{ get { return Context == null ? SourceSpan.Invalid : Context.Trace.CurrentSpan; } }

        //public string GetCurrentLocation()
        //{
        //    if (!CurrentBlock.IsValid)
        //        return "invalid location";
        //    return string.Format("line {0}, column {1}", CurrentBlock.Start.Line, CurrentBlock.Start.Column);
        //}
        
        ///// <summary>
        ///// Gets the actual lines of code that are currently being executed
        ///// </summary>
        ///// <param name="source">
        ///// The source unit representing the code that is being executed
        ///// </param>
        ///// <returns>
        ///// Returns the code that is being executed
        ///// </returns>
        //public string GetCurrentCode(SourceUnit source)
        //{
        //    return GetSourceLines(source, CurrentBlock);
        //}
        
        ///// <summary>
        ///// Gets the actual lines of code that are currently being executed
        ///// </summary>
        ///// <param name="source">
        ///// The code that is being executed
        ///// </param>
        ///// <returns>
        ///// Returns the code that is being executed
        ///// </returns>
        //public string GetCurrentCode(string source)
        //{
        //    if (!CurrentBlock.IsValid)
        //        return "invalid location";
        //    return source.Substring(CurrentBlock.Start.Index, CurrentBlock.Length);
        //}

        private string GetSourceLines(SourceSpan span)
        {
            if (!span.IsValid)
                return "invalid location";

            string[] codeLines = Context.Source.GetCodeLines(span.Start.Line, span.End.Line - span.Start.Line);
            string code = "";
            foreach (var line in codeLines)
                code += line + "\n";
            return code.Trim();
        }

        private string GetSourceLine(SourceUnit source, SourceSpan span)
        {
            return source.GetCodeLine(span.Start.Line);
        }

        private string GetSourceCode(string source, SourceSpan span)
        {
            if (!span.IsValid)
                return "invalid location";

            char[] buffer = new char[span.Length];

            return source.Substring(span.Start.Index, span.Length);
        }

        private string GetSourceCode(SourceUnit source, SourceSpan span)
        {
            if (!span.IsValid)
                return "invalid location";

            char[] buffer = new char[span.Length];

            using (var reader = source.GetReader())
            {
                reader.Read(buffer, span.Start.Index, buffer.Length);
                return new string(buffer);
            }
        }

        private Stack<FunctionStack> stack;

        protected void UnwindStack(int depth)
        {
            int unwound = 0;
            while (unwound++ < depth && stack.Count > 0)
                stack.Pop();
        }

        protected string FormatStackTrace(Exception ex)
        {
            string[] clrTrace = ex.StackTrace.Split('\n').Select(x => x.Trim().Remove(0, 2).Trim()).ToArray();

            string stackTrace = "";
            int clrIndex = clrTrace.Length - 1;
            for (; clrIndex >= 0; clrIndex--)            
                stackTrace = "[CLR]: " + clrTrace[clrIndex] + "\n" + stackTrace;

            return stackTrace;
        }

        ///// <summary>
        ///// Gets the stack trace representing the current function call stack
        ///// </summary>
        //public virtual string GetStackTrace()
        //{
        //    FunctionStack[] stack = new FunctionStack[this.stack.Count];
        //    this.stack.CopyTo(stack, 0);

        //    string[] clrTrace = base.StackTrace.Split('\n').Select(x => x.Trim().Remove(0,2).Trim()).ToArray();

        //    string stackTrace = "";
        //    int clrIndex = clrTrace.Length - 1;
        //    for (; clrIndex >= 0; clrIndex--)
        //    {
        //        if (clrTrace[clrIndex].Equals("lambda_method(Closure , IDynamicMetaObjectProvider )"))
        //        {
        //            clrIndex--;
        //            break;
        //        }
        //        stackTrace = "[CLR]: " + clrTrace[clrIndex] + "\n" + stackTrace;
        //    }

        //    for (int i = stack.Length - 1; i >= 0; i--)
        //    {
        //        string inWhat = "";
        //        switch(stack[i].Type)
        //        {
        //            case LuaTrace.FunctionType.Lua:
        //                inWhat = "function '" + stack[i].MethodName + "'";
        //                break;
        //            case LuaTrace.FunctionType.Chunk:
        //                inWhat = "main chunk";
        //                break;
        //            case LuaTrace.FunctionType.CLR:
        //                inWhat = "CLR function '" + stack[i].MethodName + "'";
        //                break;
        //            case LuaTrace.FunctionType.Invoke:
        //                continue;   //Don't print invoke calls as part of the stack trace
        //                //inWhat = "function call '" + stack[i].MethodName + "'";
        //                //break;
        //        }

        //        stackTrace = Context.Source.Path + ":" + stack[i].FunctionLocation.Start.Line + ": in " + inWhat + "\n" + stackTrace;
        //    }

        //    //for (; clrIndex >= 0; clrIndex--)   
        //    //    stackTrace = "[CLR]: " + clrTrace[clrIndex] + "\n" + stackTrace;

        //    Exception innerEx = InnerException;
        //    while (innerEx != null)
        //    {
        //        stackTrace = FormatStackTrace(innerEx) + stackTrace;
        //        innerEx = innerEx.InnerException;
        //    }

        //    return stackTrace;
        //}

        ///// <summary>
        ///// Gets the formatted list of invoked Lua expressions leading up to this error
        ///// </summary>
        //public IEnumerable<string> CallStack
        //{
        //    get
        //    {
        //        FunctionStack[] stack = new FunctionStack[this.stack.Count];
        //        this.stack.CopyTo(stack, 0);

        //        for (int i = stack.Length - 1; i >= 0; i--)
        //        {
        //            string inWhat = "";
        //            switch (stack[i].Type)
        //            {
        //                case LuaTrace.FunctionType.Lua:
        //                    inWhat = "function '" + stack[i].MethodName + "'";
        //                    break;
        //                case LuaTrace.FunctionType.Chunk:
        //                    inWhat = "main chunk";
        //                    break;
        //                case LuaTrace.FunctionType.CLR:
        //                    inWhat = "CLR function '" + stack[i].MethodName + "'";
        //                    break;
        //                case LuaTrace.FunctionType.Invoke:
        //                    continue;   //Don't print invoke calls as part of the stack trace
        //                //inWhat = "function call '" + stack[i].MethodName + "'";
        //                //break;
        //            }

        //            yield return stack[i].FileName + ":" + stack[i].FunctionLocation.Start.Line + ": in " + inWhat;
        //        }
        //    }
        //}

        ///// <summary>
        ///// <inheritdoc/>
        ///// </summary>
        //public override string Message
        //{
        //    get
        //    {
        //        return Context.Trace.CurrentDocument + ":" + Context.Trace.CurrentSpan.Start.Line + ":" + base.Message;
        //    }
        //}

        ///// <summary>
        ///// <inheritdoc/>
        ///// </summary>
        //public override string Source
        //{
        //    get
        //    {
        //        if (!CurrentBlock.IsValid || !CurrentBlock.Start.IsValid)
        //            return "Lua Code";
        //        return string.Format("Lua Code: line {0}, column {1}", CurrentBlock.Start.Line, CurrentBlock.Start.Column);
        //    }
        //    set
        //    {
        //        throw new InvalidOperationException();
        //    }
        //}

        ///// <summary>
        ///// <inheritdoc/>
        ///// </summary>
        //public override string StackTrace
        //{
        //    get
        //    {
        //        return GetStackTrace();
        //    }
        //}

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public override string ToString()
        {
            return base.ToString();
        }
    }

    public class LuaErrorException : LuaRuntimeException
    {
        internal LuaErrorException(CodeContext context, object errorObject, int stackLevel = 1, Exception innerException = null)
            : base(context, BaseLibrary.ToStringEx(errorObject), innerException)
        {
            Result = errorObject;
            StackLevel = stackLevel;

            UnwindStack(StackLevel);
        }

        /// <summary>
        /// Gets the level on the call stack at which the exception was generated
        /// </summary>
        public int StackLevel
        { get; private set; }

        /// <summary>
        /// Gets the object associated with the error call in the message parameter
        /// </summary>
        public object Result
        { get; private set; }
    }
}
