﻿using IronLua.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Scripting.Actions;

namespace IronLua.Library
{
    class InteropLibrary : Library
    {
        public InteropLibrary(LuaContext context, params Type[] types)
            : base(context)
        {

        }
        
        public override void Setup(Runtime.LuaTable table)
        {
            table.SetConstant("import", (Func<string, LuaTable>)ImportType);
            table.SetConstant("method", (Func<object, string, object>)InteropGetMethod);
            table.SetConstant("call", (Func<object, string, Varargs, object>)InteropCallMethod);
            table.SetConstant("setvalue", (Func<object, string, object, object>)InteropSetValue);
            table.SetConstant("getvalue", (Func<object, string, object>)InteropGetValue);
            table.SetConstant("subscribe", (Action<object, string, Delegate>)InteropSubscribeEvent);
            table.SetConstant("unsubscribe", (Action<object, string, Delegate>)InteropUnsubscribeEvent);
        }

        private LuaTable ImportType(string typeName)
        {
            var type = Type.GetType(typeName, false);
            if(type != null)
            {
                var table = new LuaTable(Context);
                table.SetConstant("__clrtype", type);
                table.Metatable = GenerateMetatable();

                Context.SetTypeMetatable(type, table.Metatable);

                return table;
            }
            return null;
        }

        public void ImportType(Type type)
        {
            Context.SetTypeMetatable(type, GenerateMetatable());
        }

        internal LuaTable GenerateMetatable()
        {
            LuaTable table = new LuaTable(Context);
            table.SetConstant(Constant.INDEX_METAMETHOD, (Func<object, object, object>)InteropIndex);
            table.SetConstant(Constant.NEWINDEX_METAMETHOD, (Func<object, object, object, object>)InteropNewIndex);
            table.SetConstant(Constant.CALL_METAMETHOD, (Func<object, Varargs, object>)InteropCall);
            table.SetConstant(Constant.CONCAT_METAMETHOD, (Func<string, LuaTable, string>)Concat);
            table.SetConstant(Constant.TOSTRING_METAFIELD, (Func<LuaTable, string>)ToString);

            return table;
        }

        private static string Concat(string str, LuaTable table)
        {
            return str + (table.GetValue("__clrtype") as Type).FullName;
        }

        private static string ToString(LuaTable table)
        {
            return "[CLASS] " + (table.GetValue("__clrtype") as Type).FullName;
        }

        private static object InteropIndex(object target, object index)
        {
            var type = target.GetType();

            

            return null;
        }

        private static object InteropNewIndex(object target, object index, object value)
        {
            var type = (target as LuaTable).GetValue("__clrtype") as Type;
            var properties = type.GetProperties(
                BindingFlags.SetProperty |
                BindingFlags.SetField |
                BindingFlags.Public |
                BindingFlags.Static);

            //First check if there are any properties/fields with the specified name
            string indexKey = index.ToString();
            var property = properties.FirstOrDefault(x => x.Name.Equals(indexKey));
            if (property != default(PropertyInfo))
            {
                property.SetValue(target, value, null);
                return value;
            }

            //Then check if there are any indexers on the object and try them
            property = properties.FirstOrDefault(x => x.GetIndexParameters().Length == 1);
            if (property != default(PropertyInfo))
            {
                property.SetValue(target, value, new[] { index });
                return value;
            }

            throw new LuaRuntimeException("Undefined field or property '{0}' on {1}", indexKey, type.FullName);
        }

        /// <summary>
        /// Acts upon a call to the specified interop type object. Behaves like a constructor call
        /// </summary>
        /// <param name="target">The interop type object being called</param>
        /// <param name="parameters">The parameters passed to the constructor</param>
        /// <returns>Returns the new instance of the interop type</returns>
        private static object InteropCall(object target, Varargs parameters)
        {
            //CLR class reference (static references)
            if (target is LuaTable)
            {
                var type = (target as LuaTable).GetValue("__clrtype") as Type;

                var args = parameters.ToArray();
                var argsTypes = args.Select(x => x.GetType()).ToArray();

                return Activator.CreateInstance(type, args);
            }

            //CLR instance reference
            else if (target is BoundMemberTracker)
            {
                var tracker = target as BoundMemberTracker;

                var type = tracker.ObjectInstance.GetType();

                var methodName = tracker.Name;
                var argsTypes = parameters.Select(x => x.GetType()).ToArray();

                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                .Where(x => x.Name.Equals(methodName) && ParamsMatch(x, argsTypes));

                if (methods.Count() != 1)
                    throw new LuaRuntimeException("Could not find a unique method '{0}' on {1}", methodName, type.FullName);

                return methods.First().Invoke(tracker.ObjectInstance, parameters.Skip(1).ToArray());
            }

            throw new LuaRuntimeException("Attempting to execute an anonymous function on the given type, this is not possible");
        }



        #region Method Calls

        private object InteropCallMethod(object target, string methodName, Varargs parameters = null)
        {
            if (target is LuaTable)
            {
                var type = (target as LuaTable).GetValue("__clrtype") as Type;
                var paramTypes = parameters == null ? new Type[0] : parameters.Select(x => x.GetType()).ToArray();

                var methods = type.GetMethods(BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                    .Where(x => x.Name.Equals(methodName))
                    .Where(x => ParamsMatch(x, paramTypes)).ToArray();
                if (methods.Length != 1)
                    throw new LuaRuntimeException("Could not find a method with the given parameters");

                return methods[0].Invoke(null, parameters == null ? new object[0] : parameters.ToArray());
            }
            else
            {
                var type = target.GetType();
                var paramTypes = parameters == null ? new Type[0] : parameters.Select(x => x.GetType()).ToArray();

                var methods = type.GetMethods(BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                    .Where(x => x.Name.Equals(methodName))
                    .Where(x => ParamsMatch(x, paramTypes)).ToArray();
                if (methods.Length != 1)
                    throw new LuaRuntimeException("Could not find a method with the given parameters");

                return methods[0].Invoke(target, parameters == null ? new object[0] : parameters.ToArray());
            }
        }

        private object InteropGetMethod(object target, string methodName)
        {
            if (target is LuaTable)
            {
                var type = (target as LuaTable).GetValue("__clrtype") as Type;
                var methods = type.GetMethods(BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                    .Where(x => x.Name.Equals(methodName)).ToArray();
                if (methods.Length > 0)
                {
                    var methodTable = new LuaTable(Context);
                    methodTable.SetConstant("__target", null);
                    methodTable.SetConstant("__clrtype", type);
                    methodTable.SetConstant("__method", methodName);
                    foreach (var method in methods)
                        methodTable.SetConstant(method, method);
                    methodTable.Metatable = GenerateMethodMetaTable(Context);

                    return methodTable;
                }

            }
            else
            {
                var type = target.GetType();

                var methods = type.GetMethods(BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                    .Where(x => x.Name.Equals(methodName)).ToArray();
                if (methods.Length > 0)
                {
                    var methodTable = new LuaTable(Context);
                    methodTable.SetConstant("__target", target);
                    methodTable.SetConstant("__clrtype", type);
                    methodTable.SetConstant("__method", methodName);
                    foreach (var method in methods)
                        methodTable.SetConstant(method, method);
                    methodTable.Metatable = GenerateMethodMetaTable(Context);

                    return methodTable;
                }
            }
            return null;
        }

        private static bool ParamsMatch(MethodInfo method, Type[] paramTypes)
        {
            var parameters = method.GetParameters();
            bool hasParams = false;
            for (int i = 0; i < parameters.Length; i++)
            {
                //Test if there are too many required parameters
                if (i >= paramTypes.Length && !parameters[i].IsOptional)
                    return false;

                //Test if the parameter's types match or not
                if (paramTypes[i] != parameters[i].ParameterType &&
                    !parameters[i].ParameterType.IsAssignableFrom(paramTypes[i]) &&
                    !(hasParams = (i == parameters.Length - 1))) //This is in case we have a possible params argument
                    return false;
            }

            //If we have checked everything, then it's fine
            if (!hasParams)
                return true;

            //Check if we have any params args...
            var paramsType = parameters.Last().ParameterType;
            if (!paramsType.IsArray)
                return false;

            var paramItemsType = paramsType.GetElementType();
            for (int i = parameters.Length - 1; i < paramTypes.Length; i++)

                //Make sure that all the params arguments are valid
                if (!paramItemsType.IsAssignableFrom(paramTypes[i]))
                    return false;

            return true;
        }

        private static LuaTable GenerateMethodMetaTable(LuaContext context)
        {
            var table = new LuaTable(context);
            table.SetConstant(Constant.CALL_METAMETHOD, (Func<object, Varargs, object>)MethodInteropCall);
            table.SetConstant(Constant.TOSTRING_METAFIELD, (Func<LuaTable, string>)MethodTableToString);
            return table;
        }

        private static string MethodTableToString(LuaTable table)
        {
            return string.Format("{0}.{1}(...)", (table.GetValue("__clrtype") as Type).FullName, table.GetValue("__method"));
        }

        private static object MethodInteropCall(object target, Varargs parameters)
        {
            var table = target as LuaTable;

            var paramTypes = parameters.Select(x => x.GetType()).ToArray();

            Varargs pair = table.Next();
            do
            {
                var methodInfo = pair[0] as MethodInfo;
                if (methodInfo == null)
                    continue;

                if (ParamsMatch(methodInfo, paramTypes))
                    return methodInfo.Invoke(table.GetValue("__target"), parameters.ToArray());

            } while ((pair = table.Next(pair[0])) != null);

            throw new LuaRuntimeException("Could not find a method with the given parameters");
        }

        #endregion

        #region Get/Set Value

        private static object InteropGetValue(object table, string propertyName)
        {
            //Static calls
            if (table is LuaTable)
            {
                var type = (table as LuaTable).GetValue("__clrtype") as Type;

                var property = type.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Public);
                if (property != null)
                    return property.GetValue(null, null);

                var field = type.GetField(propertyName, BindingFlags.Static | BindingFlags.Public);
                if (field != null)
                    return field.GetValue(null);

                throw new LuaRuntimeException("The static field or property '{0}' was not found", propertyName);
            }

            //Instance calls
            else
            {
                var type = table.GetType();

                var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (property != null)
                    return property.GetValue(table, null);

                var field = type.GetField(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (field != null)
                    return field.GetValue(table);

                throw new LuaRuntimeException("The instance field or property '{0}' was not found", propertyName);
            }
        }
        
        private static object InteropSetValue(object table, string propertyName, object value)
        {
            //Static calls
            if (table is LuaTable)
            {
                var type = (table as LuaTable).GetValue("__clrtype") as Type;

                var property = type.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Public);
                if (property != null)
                {
                    property.SetValue(null, value, null);
                    return value;
                }

                var field = type.GetField(propertyName, BindingFlags.Static | BindingFlags.Public);
                if (field != null)
                {
                    field.SetValue(null, value);
                    return value;
                }

                throw new LuaRuntimeException("The static field or property '{0}' was not found", propertyName);
            }

            //Instance calls
            else
            {
                var type = table.GetType();

                var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (property != null)
                {
                    property.SetValue(table, value, null);
                    return value;
                }

                var field = type.GetField(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (field != null)
                {
                    field.SetValue(table, value);
                    return value;
                }

                throw new LuaRuntimeException("The static field or property '{0}' was not found", propertyName);
            }
        }
        
        #endregion

        #region Event Handlers

        private static void InteropSubscribeEvent(object target, string eventName, Delegate handler)
        {
            
            //Static events
            if (target is LuaTable)
            {
                var type = (target as LuaTable).GetValue("__clrtype") as Type;

                var eventSource = type.GetEvent(eventName, BindingFlags.Static | BindingFlags.Public);
                if (eventSource != null)
                {
                    Delegate safeHandler = GetEventHandlerDelegate(eventSource.EventHandlerType, handler);
                    eventSource.AddEventHandler(null, safeHandler);
                    return;
                }

                throw new LuaRuntimeException("The static event '{0}' was not found", eventName);
            }

            //Instance events
            else
            {
                var type = target.GetType();

                var eventSource = type.GetEvent(eventName, BindingFlags.Instance | BindingFlags.Public);
                if (eventSource != null)
                {
                    Delegate safeHandler = GetEventHandlerDelegate(eventSource.EventHandlerType, handler);
                    eventSource.AddEventHandler(target, safeHandler);
                    return;
                }

                throw new LuaRuntimeException("The instance event '{0}' was not found", eventName);
            }
        }

        private static void InteropUnsubscribeEvent(object target, string eventName, Delegate handler)
        {
            //Static events
            if (target is LuaTable)
            {
                var type = (target as LuaTable).GetValue("__clrtype") as Type;

                var eventSource = type.GetEvent(eventName, BindingFlags.Static | BindingFlags.Public);
                if (eventSource != null)
                {
                    Delegate safeHandler = GetEventHandlerDelegate(eventSource.EventHandlerType, handler);
                    eventSource.AddEventHandler(null, safeHandler);
                    return;
                }

                throw new LuaRuntimeException("The static event '{0}' was not found", eventName);
            }

            //Instance events
            else
            {
                var type = target.GetType();

                var eventSource = type.GetEvent(eventName, BindingFlags.Instance | BindingFlags.Public);
                if (eventSource != null)
                {
                    Delegate safeHandler = GetEventHandlerDelegate(eventSource.EventHandlerType, handler);
                    eventSource.AddEventHandler(target, safeHandler);
                    return;
                }

                throw new LuaRuntimeException("The instance event '{0}' was not found", eventName);
            }
        }

        private static Delegate GetEventHandlerDelegate(Type eventType, Delegate handler)
        {
            return handler;
            //return Delegate.CreateDelegate(eventType, handler.Target, handler.Method);
        }

        #endregion

    }
}