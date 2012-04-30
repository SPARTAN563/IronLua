using System;
using System.Dynamic;
using System.Linq.Expressions;
using IronLua.Library;
using Expr = System.Linq.Expressions.Expression;

namespace IronLua.Runtime.Binder
{
    class LuaConvertBinder : ConvertBinder
    {
        public LuaConvertBinder(Type type)
            : base(type, false)
        {
        }

        public override DynamicMetaObject FallbackConvert(DynamicMetaObject target, DynamicMetaObject errorSuggestion)
        {
            Expr expression = null;
            if (Type == typeof(double))
                expression = ToNumber(target);
            else if (Type == typeof(bool))
                expression = ToBool(target);

            if (expression == null)
                throw new InvalidOperationException();

            return new DynamicMetaObject(expression, RuntimeHelpers.MergeTypeRestrictions(target));
        }

        static Expression ToBool(DynamicMetaObject target)
        {
            if (target.LimitType == typeof(bool))
                return Expr.Convert(target.Expression, typeof(bool));

            if (target.LimitType.IsValueType)
                return Expr.Constant(true); // all value types resolve to true

            return Expr.NotEqual(target.Expression, Expr.Constant(null));
        }

        public static Expression ToNumber(DynamicMetaObject target)
        {
            if (target.LimitType == typeof(double))
                return Expr.Convert(target.Expression, typeof(double));
            if (target.LimitType == typeof(string))
                return
                    Expression.Invoke(
                        Expression.Constant((Func<string, double, double>) BaseLibrary.InternalToNumber),
                        target.Expression, Expression.Constant(10.0));

            return null;
        }
    }
}