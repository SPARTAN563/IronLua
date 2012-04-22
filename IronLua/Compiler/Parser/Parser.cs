using System;
using System.Collections.Generic;
using IronLua.Compiler.Ast;
using IronLua.Library;
using Microsoft.Scripting.Utils;

namespace IronLua.Compiler.Parser
{
    class Parser
    {
        const int UNARY_OP_PRIORITY = 8;

        static readonly Dictionary<Symbol, UnaryOp> unaryOps =
            new Dictionary<Symbol, UnaryOp>
                {
                    {Symbol.Minus, UnaryOp.Negate},
                    {Symbol.Not,   UnaryOp.Not},
                    {Symbol.Hash,  UnaryOp.Length}
                };

        static readonly Dictionary<Symbol, BinaryOp> binaryOps =
            new Dictionary<Symbol, BinaryOp>
                {
                    {Symbol.Or,           BinaryOp.Or},
                    {Symbol.And,          BinaryOp.And},
                    {Symbol.EqualEqual,   BinaryOp.Equal},
                    {Symbol.TildeEqual,   BinaryOp.NotEqual},
                    {Symbol.Less,         BinaryOp.Less},
                    {Symbol.Greater,      BinaryOp.Greater},
                    {Symbol.LessEqual,    BinaryOp.LessEqual},
                    {Symbol.GreaterEqual, BinaryOp.GreaterEqual},
                    {Symbol.DotDot,       BinaryOp.Concat},
                    {Symbol.Plus,         BinaryOp.Add},
                    {Symbol.Minus,        BinaryOp.Subtract},
                    {Symbol.Star,         BinaryOp.Multiply},
                    {Symbol.Slash,        BinaryOp.Divide},
                    {Symbol.Percent,      BinaryOp.Mod},
                    {Symbol.Caret,       BinaryOp.Power}
                };

        static readonly Dictionary<BinaryOp, Tuple<int, int>> binaryOpPriorities =
            new Dictionary<BinaryOp, Tuple<int, int>>
                {
                    {BinaryOp.Or,           new Tuple<int, int>(1, 1)},
                    {BinaryOp.And,          new Tuple<int, int>(2, 2)},
                    {BinaryOp.Less,         new Tuple<int, int>(3, 3)},
                    {BinaryOp.Greater,      new Tuple<int, int>(3, 3)},
                    {BinaryOp.LessEqual,    new Tuple<int, int>(3, 3)},
                    {BinaryOp.GreaterEqual, new Tuple<int, int>(3, 3)},
                    {BinaryOp.NotEqual,     new Tuple<int, int>(3, 3)},
                    {BinaryOp.Equal,        new Tuple<int, int>(3, 3)},
                    {BinaryOp.Concat,       new Tuple<int, int>(5, 4)}, // Left associative
                    {BinaryOp.Add,          new Tuple<int, int>(6, 6)},
                    {BinaryOp.Subtract,     new Tuple<int, int>(6, 6)},
                    {BinaryOp.Multiply,     new Tuple<int, int>(7, 7)},
                    {BinaryOp.Divide,       new Tuple<int, int>(7, 7)},
                    {BinaryOp.Mod,          new Tuple<int, int>(7, 7)},
                    {BinaryOp.Power,        new Tuple<int, int>(9, 8)}  // Left associative
                };

        readonly ILexer lexer;

        public Parser(ILexer lexer)
        {
            ContractUtils.RequiresNotNull(lexer, "lexer");

            this.lexer = lexer;
        }

        public Block Parse()
        {
            var block = Block();
            lexer.Expect(Symbol.Eof);
            return block;
        }

        /* Parses identifierList
         * identifier {',' identifier} */
        List<string> IdentifierList()
        {
            var identifiers = new List<string> {lexer.ExpectLexeme(Symbol.Identifier)};

            while (lexer.TryConsume(Symbol.Comma))
                identifiers.Add(lexer.ExpectLexeme(Symbol.Identifier));

            return identifiers;
        }

        /* Parses experssionList
         * expression {',' expression } */
        List<Expression> ExpressionList()
        {
            var expressions = new List<Expression> { Expression() };

            while (lexer.TryConsume(Symbol.Comma))
                expressions.Add(Expression());

            return expressions;
        }

        /* Parses variableList
         * (oldVariable | variable) {',' variable } */
        List<Variable> VariableList(Variable oldVariable = null)
        {
            var variables = new List<Variable> {oldVariable ?? Variable()};

            while (lexer.TryConsume(Symbol.Comma))
                variables.Add(Variable());

            return variables;
        }

        /* Parses table
         * [field {fieldsep field} [fieldsep]]
         * fieldsep := ',' | ';' */
        Expression.Table Table()
        {
            lexer.Expect(Symbol.LeftBrace);
            var fields = new List<Field>();

            var loop = true;
            while (loop)
            {
                if (lexer.CurrentSymbol == Symbol.RightBrace)
                    break;
                fields.Add(Field());

                switch (lexer.CurrentSymbol)
                {
                    case Symbol.Comma:
                    case Symbol.SemiColon:
                        lexer.Consume();
                        break;
                    case Symbol.RightBrace:
                        loop = false;
                        break;
                    default:
                        throw lexer.SyntaxException(ExceptionMessage.UNEXPECTED_SYMBOL, lexer.CurrentSymbol);
                }
            }

            lexer.Expect(Symbol.RightBrace);
            return new Expression.Table(fields);
        }

        /* Parses Number */
        double NumberLiteral()
        {
            var number = lexer.ExpectLexeme(Symbol.Number);
            double result;
            bool successful = number.StartsWith("0x") ?
                NumberUtil.TryParseHexNumber(number.Substring(2), true, out result) :
                NumberUtil.TryParseDecimalNumber(number, out result);

            if (successful)
                return result;

            // Check if value is well formed!   Stuff like 10e500 return +INF
            var fields = number.Split('e', 'E');
            if (fields.Length == 2)
            {
                int v1, v2;
                bool b1 = Int32.TryParse(fields[0], out v1);
                bool b2 = Int32.TryParse(fields[1], out v2);

                if (b1 && b2)
                {
                    return Math.Sign(v1) > 0 ? Double.PositiveInfinity : Double.NegativeInfinity;
                }
            }

            throw lexer.SyntaxException(ExceptionMessage.MALFORMED_NUMBER, number);
        }


        /* Parses variable
         * Identifier |
         * prefixExpression '[' expression ']' |
         * prefixExpression '.' Identifier */
        Variable Variable()
        {
            var variable = PrefixExpression().LiftVariable();
            if (variable == null)
                throw lexer.SyntaxException(ExceptionMessage.UNEXPECTED_SYMBOL, lexer.CurrentSymbol);
            return variable;
        }

        /* Parses field
         * '[' expression ']' '=' expression |
         * Identifier '=' expression | 
         * expression */
        Field Field()
        {
            switch (lexer.CurrentSymbol)
            {
                case Symbol.LeftBrack:
                    lexer.Consume();
                    var member = Expression();
                    lexer.Expect(Symbol.RightBrack);
                    lexer.Expect(Symbol.Equal);
                    var value = Expression();
                    return new Field.MemberExpr(member, value);

                default:
                    var expression = Expression();
                    if (lexer.CurrentSymbol != Symbol.Equal)
                        return new Field.Normal(expression);

                    lexer.Consume();
                    var memberId = expression.LiftIdentifier();
                    if (memberId != null)
                        return new Field.MemberId(memberId, Expression());
                    throw lexer.SyntaxException(ExceptionMessage.UNEXPECTED_SYMBOL, lexer.CurrentSymbol);
            }
        }

        /* Parses elseif
         * 'elseif' expression 'then' block */
        Elseif Elseif()
        {
            lexer.Expect(Symbol.Elseif);
            var test = Expression();
            lexer.Expect(Symbol.Then);
            var body = Block();
            return new Elseif(test, body);
        }

        /* Parses arguments
         * '(' expressionList ')' | table | String */
        Arguments Arguments()
        {
            switch (lexer.CurrentSymbol)
            {
                case Symbol.LeftParen:
                    if (lexer.Current.Line != lexer.Last.Line)
                        throw lexer.SyntaxException(ExceptionMessage.AMBIGUOUS_SYNTAX_FUNCTION_CALL);
                    lexer.Consume();

                    var arguments = new List<Expression>();
                    if (lexer.CurrentSymbol != Symbol.RightParen)
                        arguments = ExpressionList();

                    lexer.Expect(Symbol.RightParen);
                    return new Arguments.Normal(arguments);

                case Symbol.LeftBrace:
                    return new Arguments.Table(Table());

                case Symbol.String:
                    var str = new Expression.String(lexer.ExpectLexeme(Symbol.String));
                    return new Arguments.String(str);

                default:
                    throw lexer.SyntaxException(ExceptionMessage.UNEXPECTED_SYMBOL, lexer.CurrentSymbol);
            }
        }

        /* Parses functionBody
         * '(' [identifierList [',' '...'] | '...'] ')' block 'end' */
        FunctionBody FunctionBody()
        {
            lexer.Expect(Symbol.LeftParen);

            var parameters = new List<string>();
            var varargs = false;
            
            if (lexer.CurrentSymbol != Symbol.RightParen)
            {
                if (lexer.TryConsume(Symbol.DotDotDot))
                {
                    varargs = true;
                }
                else
                {
                    parameters.Add(lexer.ExpectLexeme(Symbol.Identifier));

                    while (!varargs && lexer.TryConsume(Symbol.Comma))
                    {
                        if (lexer.CurrentSymbol == Symbol.Identifier)
                            parameters.Add(lexer.ConsumeLexeme());
                        else if (lexer.TryConsume(Symbol.DotDotDot))
                            varargs = true;
                    }
                }
            }

            lexer.Expect(Symbol.RightParen);
            var body = Block();
            lexer.Expect(Symbol.End);
            return new FunctionBody(parameters, varargs, body);
        }

        /* Parses functionName
         * Identifier {'.' Identifier} [':' Identifer] */
        FunctionName FunctionName()
        {
            var identifiers = new List<string> { lexer.ExpectLexeme(Symbol.Identifier) };

            while (lexer.TryConsume(Symbol.Comma))
                identifiers.Add(lexer.ExpectLexeme(Symbol.Identifier));

            var table = lexer.TryConsume(Symbol.Colon) ? lexer.ExpectLexeme(Symbol.Identifier) : null;

            return new FunctionName(identifiers, table);
        }

        /* Parses prefixExpression
         * variable | functionCall | '(' expression ')' */
        PrefixExpression PrefixExpression()
        {
            // Parse the terminal/first symbol of the prefixExpression
            PrefixExpression left;
            switch (lexer.CurrentSymbol)
            {
                case Symbol.Identifier:
                    left = new PrefixExpression.Variable(new Variable.Identifier(lexer.ConsumeLexeme()));
                    break;
                case Symbol.LeftParen:
                    lexer.Consume();
                    left = new PrefixExpression.Expression(Expression());
                    lexer.ExpectLexeme(Symbol.RightParen);
                    break;
                default:
                    throw lexer.SyntaxException(ExceptionMessage.UNEXPECTED_SYMBOL, lexer.CurrentSymbol);
            }

            while (true)
            {
                string identifier;
                switch (lexer.CurrentSymbol)
                {
                    case Symbol.LeftBrack:
                        lexer.Consume();
                        left = new PrefixExpression.Variable(new Variable.MemberExpr(left, Expression()));
                        lexer.Expect(Symbol.RightBrack);
                        break;

                    case Symbol.Dot:
                        lexer.Consume();
                        identifier = lexer.ExpectLexeme(Symbol.Identifier);
                        left = new PrefixExpression.Variable(new Variable.MemberId(left, identifier));
                        break;

                    case Symbol.Colon:
                        lexer.Consume();
                        identifier = lexer.ExpectLexeme(Symbol.Identifier);
                        var arguments = Arguments();
                        left = new PrefixExpression.FunctionCall(new FunctionCall.Table(left, identifier, arguments));
                        break;

                    case Symbol.LeftParen:
                    case Symbol.LeftBrace:
                    case Symbol.String:
                        left = new PrefixExpression.FunctionCall(new FunctionCall.Normal(left, Arguments()));
                        break;

                    // Unrecognized symbol, return what we have so far
                    default:
                        return left;
                }
            }
        }

        /* Parses expression
         * 'nil' | 'true' | 'false' | Number | String | '...' | function | prefixExpression |
         * table | expression BinaryOp expression | UnaryOp expression */
        Expression Expression()
        {
            var left = SimpleExpression();

            while (true)
            {
                left = BinaryExpression(left, 0);
                if (!binaryOps.ContainsKey(lexer.CurrentSymbol))
                    break;
            }

            return left;
        }

        /* Helper for parsing expressions */
        Expression SimpleExpression()
        {
            switch (lexer.CurrentSymbol)
            {
                case Symbol.Nil:
                    lexer.Consume();
                    return new Expression.Nil();
                case Symbol.True:
                    lexer.Consume();
                    return new Expression.Boolean(true);
                case Symbol.False:
                    lexer.Consume();
                    return new Expression.Boolean(false);
                case Symbol.Number:
                    return new Expression.Number(NumberLiteral());
                case Symbol.String:
                    return new Expression.String(lexer.ConsumeLexeme());
                case Symbol.DotDotDot:
                    lexer.Consume();
                    return new Expression.Varargs();
                case Symbol.Function:
                    lexer.Consume();
                    return new Expression.Function(FunctionBody());
                case Symbol.Identifier:
                case Symbol.LeftParen:
                    return new Expression.Prefix(PrefixExpression());
                case Symbol.LeftBrace:
                    return Table();
                
                default:
                    UnaryOp unaryOp;
                    if (!unaryOps.TryGetValue(lexer.CurrentSymbol, out unaryOp))
                        throw lexer.SyntaxException(ExceptionMessage.UNEXPECTED_SYMBOL, lexer.CurrentSymbol);

                    lexer.Consume();
                    var expression = BinaryExpression(SimpleExpression(), UNARY_OP_PRIORITY);
                    return new Expression.UnaryOp(unaryOp, expression);
            }
        }

        /* Helper for parsing expressions */
        Expression BinaryExpression(Expression left, int limit)
        {
            BinaryOp binaryOp;
            if (!binaryOps.TryGetValue(lexer.CurrentSymbol, out binaryOp))
                return left;
            
            // Recurse while having higher binding
            var priority = binaryOpPriorities[binaryOp];
            if (priority.Item1 < limit)
                return left;

            lexer.Consume();
            var right = BinaryExpression(SimpleExpression(), priority.Item2);

            return new Expression.BinaryOp(binaryOp, left, right);
        }

        /* Parses block
         * {do | while | repeat | if | for | function | local | assignOrFunctionCall}
         * [return | break] */
        Block Block()
        {
            var statements = new List<Statement>();
            LastStatement lastStatement = null;
            bool continueBlock = true;

            while (continueBlock)
            {
                switch (lexer.CurrentSymbol)
                {
                    case Symbol.Do:
                        statements.Add(Do());
                        break;
                    case Symbol.While:
                        statements.Add(While());
                        break;
                    case Symbol.Repeat:
                        statements.Add(Repeat());
                        break;
                    case Symbol.If:
                        statements.Add(If());
                        break;
                    case Symbol.For:
                        statements.Add(For());
                        break;
                    case Symbol.Function:
                        statements.Add(Function());
                        break;
                    case Symbol.Local:
                        statements.Add(Local());
                        break;
                    case Symbol.Identifier:
                    case Symbol.LeftParen:
                        statements.Add(AssignOrFunctionCall());
                        break;
                    case Symbol.Return:
                        lastStatement = Return();
                        continueBlock = false;
                        break;
                    case Symbol.Break:
                        lastStatement = new LastStatement.Break();
                        continueBlock = false;
                        break;
                    default:
                        continueBlock = false;
                        break;
                }

                lexer.TryConsume(Symbol.SemiColon);
            }

            return new Block(statements, lastStatement);
        }

        /* Parses return
         * 'return' [expressionList] */
        LastStatement Return()
        {
            lexer.Expect(Symbol.Return);
            if (lexer.CurrentSymbol == Symbol.End)
                return new LastStatement.Return(new List<Expression>());
            return new LastStatement.Return(ExpressionList());
        }

        /* Parses assignOrFunctionCall
         * assign | functionCall */
        Statement AssignOrFunctionCall()
        {
            var prefixExpr = PrefixExpression();

            switch (lexer.CurrentSymbol)
            {
                case Symbol.Comma:
                case Symbol.Equal:
                    return Assign(prefixExpr);
                default:
                    return FunctionCall(prefixExpr);
            }
        }

        /* Parses functionCall
         * prefixExpression arguments |
         * prefixExpression ':' Identifier arguments */
        Statement FunctionCall(PrefixExpression prefixExpr)
        {
            var functionCall = prefixExpr.LiftFunctionCall();
            if (functionCall == null)
                throw lexer.SyntaxException(ExceptionMessage.UNEXPECTED_SYMBOL, lexer.CurrentSymbol);

            return new Statement.FunctionCall(functionCall);
        }

        /* Parses assign
         * variableList '=' expressionList */
        Statement Assign(PrefixExpression prefixExpr)
        {
            var variable = prefixExpr.LiftVariable();
            if (variable == null)
                throw lexer.SyntaxException(ExceptionMessage.UNEXPECTED_SYMBOL, lexer.CurrentSymbol);

            var variables = lexer.CurrentSymbol == Symbol.Comma ? VariableList(variable) : new List<Variable> {variable};
            lexer.Expect(Symbol.Equal);
            var expressions = ExpressionList();

            return new Statement.Assign(variables, expressions);
        }

        /* Parses local
         * 'local' (localFunction | localAssign) */
        Statement Local()
        {
            lexer.Expect(Symbol.Local);

            if (lexer.CurrentSymbol == Symbol.Function)
                return LocalFunction();
            if (lexer.CurrentSymbol == Symbol.Identifier)
                return LocalAssign();

            throw lexer.SyntaxException(ExceptionMessage.UNEXPECTED_SYMBOL, lexer.CurrentSymbol);
        }

        /* Parses localAssign
         * identifierList ['=' expressionList] */
        Statement LocalAssign()
        {
            var identifiers = IdentifierList();
            var values = lexer.TryConsume(Symbol.Equal) ? ExpressionList() : null;
            return new Statement.LocalAssign(identifiers, values);
        }

        /* Parses localFunction
         * 'function' Identifier functionBody */
        Statement LocalFunction()
        {
            lexer.Expect(Symbol.Function);
            var identifier = lexer.ExpectLexeme(Symbol.Identifier);
            return new Statement.LocalFunction(identifier, FunctionBody());
        }

        /* Parses function
         * 'function' functionName functionBody */
        Statement Function()
        {
            lexer.Expect(Symbol.Function);
            return new Statement.Function(FunctionName(), FunctionBody());
        }

        /* Parses for
         * 'for' (forIn | forNormal) */
        Statement For()
        {
            lexer.Expect(Symbol.For);
            if (lexer.NextSymbol == Symbol.Comma || lexer.NextSymbol == Symbol.In)
                return ForIn();
            return ForNormal();
        }

        /* Parses forNormal
         * Identifier '=' expression ',' expression [',' expression] 'do' block 'end' */
        Statement ForNormal()
        {
            var identifier = lexer.ExpectLexeme(Symbol.Identifier);
            lexer.Expect(Symbol.Equal);
            var var = Expression();
            lexer.Expect(Symbol.Comma);
            var limit = Expression();
            var step = lexer.TryConsume(Symbol.Comma) ? Expression() : null;

            lexer.Expect(Symbol.Do);
            var body = Block();
            lexer.Expect(Symbol.End);

            return new Statement.For(identifier, var, limit, step, body);
        }

        /* Parses forIn
         * identifierList 'in' expressionList 'do' block 'end' */
        Statement ForIn()
        {
            var identifiers = IdentifierList();
            lexer.Expect(Symbol.In);
            var values = ExpressionList();

            lexer.Expect(Symbol.Do);
            var body = Block();
            lexer.Expect(Symbol.End);

            return new Statement.ForIn(identifiers, values, body);
        }

        /* Parses if
         * 'if' expression 'then' block {elseif} ['else' block] 'end' */
        Statement If()
        {
            lexer.Expect(Symbol.If);
            var test = Expression();
            lexer.Expect(Symbol.Then);
            var body = Block();

            var elseifs = new List<Elseif>();
            while (lexer.CurrentSymbol == Symbol.Elseif)
                elseifs.Add(Elseif());

            var elseBody = lexer.TryConsume(Symbol.Else) ? Block() : null;

            lexer.Expect(Symbol.End);

            return new Statement.If(test, body, elseifs, elseBody);
        }

        /* Parses repeat
         * 'repeat' block 'until' expression */
        Statement Repeat()
        {
            lexer.Expect(Symbol.Repeat);
            var body = Block();
            lexer.Expect(Symbol.Until);
            var test = Expression();
            return new Statement.Repeat(body, test);
        }

        /* Parses while
         * 'while' expression 'do' block 'end' */
        Statement While()
        {
            lexer.Expect(Symbol.While);
            var test = Expression();
            lexer.Expect(Symbol.Do);
            var body = Block();
            lexer.Expect(Symbol.End);
            return new Statement.While(test, body);
        }

        /* Parses do
         * 'do' block 'end' */
        Statement Do()
        {
            lexer.Expect(Symbol.Do);
            var body = Block();
            lexer.Expect(Symbol.End);
            return new Statement.Do(body);
        }
    }
}
