﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using IronLua.Library;
using IronLua.Runtime.Binder;
using Microsoft.Scripting.Utils;
using Expr = System.Linq.Expressions.Expression;
using ExprType = System.Linq.Expressions.ExpressionType;
using System.Dynamic;
using Microsoft.Scripting;

namespace IronLua.Runtime
{
    static class LuaOps
    {
        public static bool IsTrue(object value)
        {
            return (!(value is bool) && value != null) || (value is bool && (bool)value);
        }

        public static bool Not(object value)
        {
            return value != null && (!(value is bool) || (bool)value);
        }

        public static object Length(CodeContext context, object obj)
        {
            ContractUtils.RequiresNotNull(context, "context");

            string str;
            LuaTable table;

            if ((str = obj as string) != null)
                return str.Length;
            if ((table = obj as LuaTable) != null)
                return table.Length();

            return LengthMetamethod(context, obj);
        }

        public static object Concat(CodeContext context, object left, object right)
        {
            ContractUtils.RequiresNotNull(context, "context");

            if ((left is string || left is double) && (right is double || right is string))
                return String.Concat(left, right);

            return ConcatMetamethod(context, left, right);
        }

        public static object LengthMetamethod(CodeContext context, object obj)
        {
            ContractUtils.RequiresNotNull(context, "context");

            var metamethod = GetMetamethod(context, obj, Constant.LENGTH_METAMETHOD);
            if (metamethod != null)
                return context.DynamicCache.GetDynamicCall1()(metamethod, obj);

            throw LuaRuntimeException.Create(context, ExceptionMessage.OP_TYPE_ERROR, "get length of", BaseLibrary.Type(obj));
        }

        public static object UnaryMinusMetamethod(CodeContext context, object obj)
        {
            ContractUtils.RequiresNotNull(context, "context");

            var metamethod = GetMetamethod(context, obj, Constant.UNARYMINUS_METAMETHOD);
            if (metamethod != null)
                return context.DynamicCache.GetDynamicCall1()(metamethod, obj);

            throw LuaRuntimeException.Create(context, ExceptionMessage.OP_TYPE_ERROR, "perform arithmetic on", BaseLibrary.Type(obj));
        }

        public static object IndexMetamethod(CodeContext context, object obj, object key)
        {
            ContractUtils.RequiresNotNull(context, "context");

            var metamethod = GetMetamethod(context, obj, Constant.INDEX_METAMETHOD);

            if (metamethod != null)
            {
                if (metamethod is Delegate)
                    return context.DynamicCache.GetDynamicCall2()(metamethod, obj, key);
                if (metamethod is LuaTable)
                    return context.DynamicCache.GetDynamicIndex()(obj, key);
            }

            if (obj is LuaTable)
                return null;

            throw LuaRuntimeException.Create(context, ExceptionMessage.OP_TYPE_ERROR, "index", BaseLibrary.Type(obj));
        }

        public static object NewIndexMetamethod(CodeContext context, object obj, object key, object value)
        {
            ContractUtils.RequiresNotNull(context, "context");

            var metamethod = GetMetamethod(context, obj, Constant.NEWINDEX_METAMETHOD);

            if (metamethod != null)
            {
                if (metamethod is Delegate)
                    return context.DynamicCache.GetDynamicCall3()(metamethod, obj, key, value);
                if (metamethod is LuaTable)
                    return context.DynamicCache.GetDynamicNewIndex()(obj, key, value);
            }

            if (obj is LuaTable)       
            {
                return (obj as LuaTable).SetValue(key, value);
                //var targetParam = Expr.Parameter(typeof(object));
                //var keyParam = Expr.Parameter(typeof(object));
                //var valParam = Expr.Parameter(typeof(object));

                //var expression = Expr.Call(
                //    Expr.Convert(targetParam, typeof(LuaTable)),
                //    MemberInfos.LuaTableSetValue,
                //    keyParam,
                //    valParam);

                //return Expr.Lambda<Func<object, object, object, object>>(
                //    Expr.Block(typeof(object),
                //    new[] { targetParam, keyParam, valParam },
                //    expression)
                //    , targetParam, keyParam, valParam).Compile()(obj, key, value);
            }

            throw LuaRuntimeException.Create(context, ExceptionMessage.OP_TYPE_ERROR, "index", BaseLibrary.Type(obj));
        }

        public static object CallMetamethod(CodeContext context, object obj, object[] args)
        {
            ContractUtils.RequiresNotNull(context, "context");

            var metamethod = GetMetamethod(context, obj, Constant.CALL_METAMETHOD);
            if (metamethod != null)
            {
                var array = new object[args.Length + 1];
                array[0] = obj;
                Array.Copy(args, 0, array, 1, args.Length);
                return context.DynamicCache.GetDynamicCall1()(metamethod, new Varargs(array));
            }

            throw LuaRuntimeException.Create(context, ExceptionMessage.OP_TYPE_ERROR, "call", BaseLibrary.Type(obj));
        }

        public static object ConcatMetamethod(CodeContext context, object left, object right)
        {
            ContractUtils.RequiresNotNull(context, "context");

            var metamethod = GetMetamethod(context, left, Constant.CONCAT_METAMETHOD) ??
                             GetMetamethod(context, right, Constant.CONCAT_METAMETHOD);
            if (metamethod != null)
                return context.DynamicCache.GetDynamicCall2()(metamethod, left, right);

            var typeName = left is string ? BaseLibrary.Type(left) : BaseLibrary.Type(right);
            throw LuaRuntimeException.Create(context, ExceptionMessage.OP_TYPE_ERROR, "concatenate", typeName);
        }

        public static object BinaryOpMetamethod(CodeContext context, ExprType op, object left, object right)
        {
            ContractUtils.RequiresNotNull(context, "context");

            switch (op)
            {
                case ExprType.Add:
                case ExprType.Subtract:
                case ExprType.Multiply:
                case ExprType.Divide:
                case ExprType.Modulo:
                case ExprType.Power:
                    return NumericMetamethod(context, op, left, right);

                case ExprType.GreaterThan:
                case ExprType.GreaterThanOrEqual:
                case ExprType.LessThan:
                case ExprType.LessThanOrEqual:
                    return RelationalMetamethod(context, op, left, right);

                case ExprType.Equal:
                    return EquateMetamethod(context, op, left, right);
                case ExprType.NotEqual:
                    return Not(EquateMetamethod(context, ExprType.Equal, left, right));

                default:
                    throw new ArgumentOutOfRangeException("op", "The operation " + op.ToString() + " was not defined");
            }
        }

        public static object NumericMetamethod(CodeContext context, ExprType op, object left, object right)
        {
            ContractUtils.RequiresNotNull(context, "context");

            var methodName = GetMethodName(op);

            var metamethod = GetMetamethod(context, left, methodName) ?? 
                             GetMetamethod(context, right, methodName);
            if (metamethod != null)
                return context.DynamicCache.GetDynamicCall2()(metamethod, left, right);

            var typeName = BaseLibrary.Type(BaseLibrary.ToNumber(context, left) == null ? left : right);
            throw LuaRuntimeException.Create(context, ExceptionMessage.OP_TYPE_ERROR, "perform arithmetic on", typeName);
        }
        
        public static object RelationalMetamethod(CodeContext context, ExprType op, object left, object right)
        {
            ContractUtils.RequiresNotNull(context, "context");
            
            var leftTypeName = BaseLibrary.Type(left);
            var rightTypeName = BaseLibrary.Type(right);

            //if (left.GetType() != right.GetType())
            //    throw LuaRuntimeException.Create(ExceptionMessage.OP_TYPE_WITH_ERROR, "compare", leftTypeName, rightTypeName);

            // There are no metamethods for 'a > b' and 'a >= b' so they are translated to 'b < a' and 'b <= a' respectively
            var invert = op == ExprType.GreaterThan || op == ExprType.GreaterThanOrEqual;

            var metamethod = GetRelationalMetamethod(context, op, left, right);

            if (metamethod != null)
            {
                if (invert)
                    return context.DynamicCache.GetDynamicCall2()(metamethod, right, left);
                else
                    return context.DynamicCache.GetDynamicCall2()(metamethod, left, right);
            }

            // In the absence of a '<=' metamethod, try '<', 'a <= b' is translated to 'not (b < a)'
            if (op != ExprType.LessThanOrEqual && op != ExprType.GreaterThanOrEqual)
                throw LuaRuntimeException.Create(context, ExceptionMessage.OP_TYPE_WITH_ERROR, "compare", leftTypeName, rightTypeName);

            metamethod = GetRelationalMetamethod(context, ExprType.LessThan, left, right);
            if (metamethod != null)
            {
                if (invert)
                    return Not(context.DynamicCache.GetDynamicCall2()(metamethod, right, left));
                else
                    return Not(context.DynamicCache.GetDynamicCall2()(metamethod, left, right));
            }

            if (leftTypeName == rightTypeName)
                throw LuaRuntimeException.Create(context, ExceptionMessage.OP_TYPE_TWO_ERROR, "compare", leftTypeName);
            throw LuaRuntimeException.Create(context, ExceptionMessage.OP_TYPE_WITH_ERROR, "compare", leftTypeName, rightTypeName);
        }
        
        public static object EquateMetamethod(CodeContext context, ExprType op, object left, object right)
        {
            ContractUtils.RequiresNotNull(context, "context");

            var leftTypeName = BaseLibrary.Type(left);
            var rightTypeName = BaseLibrary.Type(right);

            if (left == null && right == null)
                return true;            
            
            var metamethod = GetRelationalMetamethod(context, op, left, right);

            if (metamethod != null)
                return context.DynamicCache.GetDynamicCall2()(metamethod, left, right);
            else
                return left == null ? right.Equals(left) : left.Equals(right);
        }
        

        static object GetRelationalMetamethod(CodeContext context, ExprType op, object left, object right)
        {
            var methodName = GetMethodName(op);
            var metamethodLeft = GetMetamethod(context, left, methodName);
            var metamethodRight = GetMetamethod(context, right, methodName);
            return metamethodLeft != metamethodRight ? null : metamethodLeft;
        }

        internal static string GetMethodName(ExprType op)
        {
            string methodName;
            return Constant.METAMETHODS.TryGetValue(op, out methodName) ? methodName : null;
        }

        public static object GetMetamethod(CodeContext context, object obj, string methodName)
        {
            LuaTable table;

            if ((table = obj as LuaTable) != null)
                table = table.Metatable;
            else if (context != null)
                table = context.Language.GetTypeMetatable(obj);

            return methodName == null || table == null ? null : table.GetValue(methodName);
        }

        public static void VarargsAssign(IRuntimeVariables variables, object[] values)
        {
            var variablesArray = VarargsAssign(variables.Count, values);
            for (var i = 0; i < variables.Count; i++)
                variables[i] = variablesArray[i];
        }

        public static object[] VarargsAssign(int numVariables, object[] values)
        {
            var variables = new object[numVariables];
            AssignValuesToVariables(variables, values, 0);
            return variables;
        }

        static void AssignValuesToVariables(object[] variables, IList<object> values, int varCount)
        {
            Varargs varargs;

            for (var valueCount = 0; valueCount < values.Count && varCount < variables.Length; valueCount++, varCount++)
            {
                var value = values[valueCount];
                if ((varargs = value as Varargs) != null)
                {
                    // Expand varargs if it's the last value otherwise just take the first value in varargs
                    if (valueCount + 1 == values.Count)
                        AssignValuesToVariables(variables, varargs, varCount);
                    else
                        variables[varCount] = varargs.FirstOrDefault();
                }
                else
                {
                    variables[varCount] = value;
                }
            }
        }

        public static object GetGlobalVariable(string key, IDynamicMetaObjectProvider globals, IDynamicMetaObjectProvider libraries, ScopeStorage environment)
        {
            object obj = null;
            if (environment != null && environment.TryGetValue(key, false, out obj)) 
                return obj;

            var e = libraries as IDictionary<string, object>;
            if (e != null && e.TryGetValue(key, out obj)) 
                return obj;

            e = globals as IDictionary<string, object>;
            if (e != null && e.TryGetValue(key, out obj)) 
                return obj;
                        
            dynamic d = libraries;
            if ((obj = d[key]) != null) 
                return obj;

            d = globals;
            if ((obj = d[key]) != null) 
                return obj;

            return null;
        }
    }
}
