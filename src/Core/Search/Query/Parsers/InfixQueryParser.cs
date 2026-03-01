// Copyright (c) Microsoft. All rights reserved.
using KernelMemory.Core.Search.Query.Ast;

namespace KernelMemory.Core.Search.Query.Parsers;

/// <summary>
/// Parser for infix notation queries (SQL-like syntax).
/// Examples: content:kubernetes, tags:AI AND createdAt>=2024-01-01, (A OR B) AND NOT C
/// Uses Parlot for grammar-based parsing with full operator precedence.
/// For now, uses simplified implementation. Full Parlot grammar will be added in future iterations.
/// </summary>
public sealed class InfixQueryParser : IQueryParser
{
    /// <summary>
    /// Parse an infix query string into an AST.
    /// Simplified implementation: supports basic field:value and boolean operators.
    /// </summary>
    public QueryNode Parse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new QuerySyntaxException("Query cannot be empty");
        }

        try
        {
            // Simplified parser: use recursive descent parsing
            var tokens = Tokenize(query);
            var parser = new InfixParser(tokens);
            var result = parser.ParseExpression();

            // Check for unmatched closing parenthesis (extra tokens after valid expression)
            var current = parser.CurrentToken();

            if (current?.Type == TokenType.RightParen)
            {
                throw new QuerySyntaxException("Unexpected closing parenthesis");
            }

            return result;
        }
        catch (QuerySyntaxException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new QuerySyntaxException($"Failed to parse query: {ex.Message}", ex);
        }
    }


    /// <summary>
    /// Validate query syntax without full parsing.
    /// </summary>
    public bool Validate(string query)
    {
        try
        {
            Parse(query);
            return true;
        }
        catch (QuerySyntaxException)
        {
            return false;
        }
    }


    /// <summary>
    /// Tokenize the query string into tokens.
    /// </summary>
    private List<Token> Tokenize(string query)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < query.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(query[i]))
            {
                i++;
                continue;
            }

            // Parentheses
            if (query[i] == '(')
            {
                tokens.Add(new Token { Type = TokenType.LeftParen, Value = "(" });
                i++;
                continue;
            }

            if (query[i] == ')')
            {
                tokens.Add(new Token { Type = TokenType.RightParen, Value = ")" });
                i++;
                continue;
            }

            // Operators
            if (i + 1 < query.Length && query[i] == ':' && query[i + 1] == '~')
            {
                tokens.Add(new Token { Type = TokenType.Operator, Value = ":~" });
                i += 2;
                continue;
            }

            if (i + 1 < query.Length && query[i] == ':' && query[i + 1] == '[')
            {
                tokens.Add(new Token { Type = TokenType.Operator, Value = ":[" });
                i += 2;
                // Read array values
                var arrayValues = new List<string>();
                var arrayValue = string.Empty;

                while (i < query.Length && query[i] != ']')
                {
                    if (query[i] == ',')
                    {
                        if (!string.IsNullOrWhiteSpace(arrayValue))
                        {
                            arrayValues.Add(arrayValue.Trim());
                            arrayValue = string.Empty;
                        }
                        i++;
                    }
                    else
                    {
                        arrayValue += query[i];
                        i++;
                    }
                }

                if (!string.IsNullOrWhiteSpace(arrayValue))
                {
                    arrayValues.Add(arrayValue.Trim());
                }

                if (i < query.Length && query[i] == ']')
                {
                    i++;
                }

                tokens.Add(new Token { Type = TokenType.ArrayValue, Value = string.Join(",", arrayValues) });
                continue;
            }

            if (i + 1 < query.Length)
            {
                var twoChar = query.Substring(i, 2);

                if (twoChar == "!=" || twoChar == ">=" || twoChar == "<=" || twoChar == "==")
                {
                    tokens.Add(new Token { Type = TokenType.Operator, Value = twoChar });
                    i += 2;
                    continue;
                }
            }

            if (query[i] == ':' || query[i] == '>' || query[i] == '<')
            {
                tokens.Add(new Token { Type = TokenType.Operator, Value = query[i].ToString() });
                i++;
                continue;
            }

            // Quoted string (double or single quotes)
            if (query[i] == '"' || query[i] == '\'')
            {
                var quoteChar = query[i];
                i++;
                var start = i;

                while (i < query.Length && query[i] != quoteChar)
                {
                    i++;
                }

                tokens.Add(new Token { Type = TokenType.String, Value = query.Substring(start, i - start) });

                if (i < query.Length)
                {
                    i++; // Skip closing quote
                }
                continue;
            }

            // Identifier or keyword
            var startPos = i;

            while (i < query.Length && !char.IsWhiteSpace(query[i]) && query[i] != '(' && query[i] != ')' && query[i] != ':' && query[i] != '>' && query[i] != '<' && query[i] != '!' && query[i] != '=')
            {
                i++;
            }

            var word = query.Substring(startPos, i - startPos);

            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }

            // Check if it's a boolean operator
            if (word.Equals("AND", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(new Token { Type = TokenType.And, Value = word });
            }
            else if (word.Equals("OR", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(new Token { Type = TokenType.Or, Value = word });
            }
            else if (word.Equals("NOT", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(new Token { Type = TokenType.Not, Value = word });
            }
            else
            {
                tokens.Add(new Token { Type = TokenType.Identifier, Value = word });
            }
        }

        return tokens;
    }


    private enum TokenType
    {
        Identifier,
        String,
        Operator,
        And,
        Or,
        Not,
        LeftParen,
        RightParen,
        ArrayValue
    }


    private sealed class Token
    {
        public TokenType Type { get; set; }
        public string Value { get; set; } = string.Empty;
    }


    private sealed class InfixParser
    {
        private readonly List<Token> _tokens;
        private int _position;


        public InfixParser(List<Token> tokens)
        {
            _tokens = tokens;
            _position = 0;
        }


        public QueryNode ParseExpression()
        {
            return ParseOr();
        }


        private QueryNode ParseOr()
        {
            var left = ParseAnd();

            while (CurrentToken()?.Type == TokenType.Or)
            {
                _position++;
                var right = ParseAnd();
                left = new LogicalNode
                {
                    Operator = LogicalOperator.Or,
                    Children = [left, right]
                };
            }

            return left;
        }


        private QueryNode ParseAnd()
        {
            var left = ParseNot();

            while (CurrentToken()?.Type == TokenType.And)
            {
                _position++;
                var right = ParseNot();
                left = new LogicalNode
                {
                    Operator = LogicalOperator.And,
                    Children = [left, right]
                };
            }

            return left;
        }


        private QueryNode ParseNot()
        {
            if (CurrentToken()?.Type == TokenType.Not)
            {
                _position++;
                var operand = ParsePrimary();
                return new LogicalNode
                {
                    Operator = LogicalOperator.Not,
                    Children = [operand]
                };
            }

            return ParsePrimary();
        }


        private QueryNode ParsePrimary()
        {
            var token = CurrentToken();

            if (token == null)
            {
                throw new QuerySyntaxException("Unexpected end of query");
            }

            // Parentheses
            if (token.Type == TokenType.LeftParen)
            {
                _position++;
                var expr = ParseExpression();

                if (CurrentToken()?.Type != TokenType.RightParen)
                {
                    throw new QuerySyntaxException("Expected closing parenthesis");
                }
                _position++;
                return expr;
            }

            // Field comparison or default search
            if (token.Type == TokenType.Identifier)
            {
                var field = token.Value;
                _position++;

                // Check if followed by operator
                var opToken = CurrentToken();

                if (opToken?.Type == TokenType.Operator)
                {
                    var op = opToken.Value;
                    _position++;

                    var valueToken = CurrentToken();

                    if (valueToken == null)
                    {
                        throw new QuerySyntaxException("Expected value after operator");
                    }

                    object value;

                    if (valueToken.Type == TokenType.ArrayValue)
                    {
                        value = valueToken.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        _position++;
                    }
                    else if (valueToken.Type == TokenType.String || valueToken.Type == TokenType.Identifier)
                    {
                        value = valueToken.Value;
                        _position++;
                    }
                    else
                    {
                        throw new QuerySyntaxException($"Unexpected token type: {valueToken.Type}");
                    }

                    return new ComparisonNode
                    {
                        Field = new FieldNode { FieldPath = field.ToLowerInvariant() },
                        Operator = MapOperator(op),
                        Value = new LiteralNode { Value = value }
                    };
                }

                // No operator, treat as default search
                return new TextSearchNode
                {
                    SearchText = field,
                    Field = null
                };
            }

            // Quoted string - default search
            if (token.Type == TokenType.String)
            {
                _position++;
                return new TextSearchNode
                {
                    SearchText = token.Value,
                    Field = null
                };
            }

            throw new QuerySyntaxException($"Unexpected token: {token.Value}");
        }


        private ComparisonOperator MapOperator(string op)
        {
            return op switch
            {
                ":" or "==" => ComparisonOperator.Equal,
                "!=" => ComparisonOperator.NotEqual,
                ">=" => ComparisonOperator.GreaterThanOrEqual,
                "<=" => ComparisonOperator.LessThanOrEqual,
                ">" => ComparisonOperator.GreaterThan,
                "<" => ComparisonOperator.LessThan,
                ":~" => ComparisonOperator.Contains,
                ":[" => ComparisonOperator.In,
                _ => throw new QuerySyntaxException($"Unknown operator: {op}")
            };
        }


        public Token? CurrentToken()
        {
            return _position < _tokens.Count
                ? _tokens[_position]
                : null;
        }
    }
}
