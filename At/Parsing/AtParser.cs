﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using At.Syntax;
using static At.OperatorAssociativity;
using static At.OperatorPosition;
using static At.SyntaxFactory;

namespace At
{
public class AtParser : IDisposable
{
    const int initialPrescedence  = 0;

    public AtParser() : this(AtLexer.DefaultLexer) {}
    public AtParser(AtLexer lexer)
    {
        this.Lexer = lexer;
    }

    public AtLexer Lexer {get;}
    public OperatorDefinitionList Operators {get;}   = new OperatorDefinitionList();
    public ExpressionRuleList ExpressionRules {get;} = new ExpressionRuleList();
    public ExpressionTransformationList Transformations {get;} = new ExpressionTransformationList();

    //ParseCompilationUnit(input)
    public CompilationUnitSyntax ParseCompilationUnit(IEnumerable<char> input)
    {
        var tokens = new Limpl.Scanner<AtToken>(Lexer.Lex(input));
        var diagnostics = new List<AtDiagnostic>();
        var expressions = this.expressions(tokens,diagnostics,initialPrescedence);
        var compilationUnitSyntax = SyntaxFactory.CompilationUnit(expressions.ToList(),diagnostics);

        diagnostics.AddRange(compilationUnitSyntax.DescendantNodes()
                                                    .OfType<ExpressionClusterSyntax>()
                                                    .Select(_=> AtDiagnostic.Create(DiagnosticIds.ExpressionCluster,(AtToken) _.ChildNodes().FirstOrDefault(x=>x.IsToken),DiagnosticSeverity.Info,"Expression cluster: "+_)));   
        return compilationUnitSyntax;
    }

    public static AtParser CreateDefaultParser(AtLexer lexer = null)
    {
        var parser = new AtParser(lexer ?? AtLexer.DefaultLexer);

        parser.ExpressionRules.Add(ExpressionRule.TokenClusterSyntax);
        parser.ExpressionRules.Add(ExpressionRule.NumericLiteral);
        parser.ExpressionRules.Add(ExpressionRule.StringLiteral);
        parser.ExpressionRules.Add(ExpressionRule.Directive);
        
        parser.Operators.Add(0,OperatorDefinition.SemiColon);

        parser.Operators.Add
        (
            1,

            OperatorDefinition.StartDeclaration.AddRules
            (
                _=>_.NamespaceDeclaration,
                _=>_.VariableDeclaration,
                _=>_.MethodDeclaration,
                _=>_.TypeDeclaration
            )
        );


        parser.Operators.Add(3,OperatorDefinition.BinaryMultiply);
        parser.Operators.Add(2,OperatorDefinition.BinaryPlus);

        parser.Operators.Add(4,OperatorDefinition.Comma);

        parser.Operators.Add(5,OperatorDefinition.ColonPair);

        parser.Operators.Add(6,OperatorDefinition.CurlyBlock);

        parser.Operators.Add(7,OperatorDefinition.PostCurlyBlock);
        parser.Operators.Add(7,OperatorDefinition.PostRoundBlock);
        parser.Operators.Add(7,OperatorDefinition.PrefixDeclaration.AddRules
            (
                _=>_.NamespaceDeclaration,
                _=>_.VariableDeclaration,
                _=>_.MethodDeclaration,
                _=>_.TypeDeclaration
            )
        );

        parser.Operators.Add(8,OperatorDefinition.PostPointyBlock);

        
        parser.Operators.Add(9,OperatorDefinition.RoundBlock);

        parser.Transformations.Add(ExpressionTransformation.InvocationExpression);
       
        return parser;
    }

    //for SyntaxPattern parser
    class SyntaxPatternCommaOpDef : IOperatorDefinition
    {
        public OperatorAssociativity Associativity    => OperatorAssociativity.List;
        public OperatorPosition      OperatorPosition => Infix;
        public TokenKind             TokenKind        => TokenKind.Comma;
        public ExpressionSyntax CreateExpression(params AtSyntaxNode[] nodes) => Binary(this,nodes);
    }

    //for SyntaxPattern parser
    class SyntaxPatternPostCircumfixOpDef : ICircumfixOperatorDefinition
    {
        readonly Func<IOperatorDefinition,AtSyntaxNode[],ExpressionSyntax> createExpression;
        public SyntaxPatternPostCircumfixOpDef(TokenKind tk1, TokenKind tk2, Func<IOperatorDefinition,AtSyntaxNode[],ExpressionSyntax> e)
        {
            TokenKind = tk1;
            EndDelimiterKind = tk2;
            createExpression = e;
        }
        public OperatorAssociativity Associativity    => OperatorAssociativity.List;
        public OperatorPosition      OperatorPosition => OperatorPosition.PostCircumfix;
        public TokenKind             TokenKind {get;}
        public ExpressionSyntax CreateExpression(params AtSyntaxNode[] nodes) => createExpression(this,nodes);
        public TokenKind EndDelimiterKind {get;}
    }

    class SyntaxPatternParser : AtParser
    {
        public SyntaxPatternParser(AtLexer lexer = null) : base(lexer) { }
    }

    public static AtParser CreateSyntaxPatternParser(AtLexer lexer = null)
    {
        var parser = new SyntaxPatternParser(lexer ?? new AtLexer(AtLexer.DefaultLexer.TokenRules.Remove(TokenRule.Colon),AtLexer.DefaultLexer.TriviaRules));

        parser.ExpressionRules.Add(ExpressionRule.TokenClusterSyntax);
        parser.ExpressionRules.Add(ExpressionRule.StringLiteral);

        //x,y
        parser.Operators.Add(1,new SyntaxPatternCommaOpDef());

        //x()
        parser.Operators.Add(2,new SyntaxPatternPostCircumfixOpDef(TokenKind.OpenParenthesis,TokenKind.CloseParenthesis,(src,nodes)=>PostBlock(src,nodes[0],RoundBlock(src,nodes.Skip(1).ToArray()))));

        //x[]
        parser.Operators.Add(2,new SyntaxPatternPostCircumfixOpDef(TokenKind.OpenBracket,TokenKind.CloseBracket,(src,nodes)=>PostBlock(src,nodes[0],SquareBlock(src,nodes.Skip(1).ToArray()))));

        return parser;
    }

    //ParseExpression(input)
    public ExpressionSyntax ParseExpression(IEnumerable<char> input)
    {
        var tokens = new Limpl.Scanner<AtToken>(Lexer.Lex(input));
        var diagnostics = new List<AtDiagnostic>();

        var isEmpty = !tokens.MoveNext(); //move to first token
        Debug.Assert(isEmpty || tokens.Position==0);
        var expr = isEmpty ? new EmptyExpressionSyntax(null,null) : expression(tokens,diagnostics,initialPrescedence,-1,null,false);
        return expr;
    }

    //expressions()
    IEnumerable<ExpressionSyntax> expressions(Limpl.IScanner<AtToken> tokens,  List<AtDiagnostic> diagnostics,int prescendence)
    {
        if (tokens.Position < 0) 
            tokens.MoveNext();

        while (!tokens.End)
            yield return expression(tokens,diagnostics,prescendence,-1,null,false);      
    }

    //error()
    internal static ErrorNode error(List<AtDiagnostic> diagnostics,string diagnosticId,AtToken token,string f, params object[] args) 
    {
        diagnostics.Add(new AtDiagnostic(diagnosticId,token,DiagnosticSeverity.Error,string.Format(f,args)));

        return ErrorNode(diagnostics, string.Format(f,args),token);
    }   



    //expression()
    ExpressionSyntax expression(Limpl.IScanner<AtToken> tokens,  List<AtDiagnostic> diagnostics, int prescedence, int lastPosition, TokenKind? endDelimiterKind, bool startOperator)
    {           
        Func<string> _trace = ()=>
        {
            return ($"expression(currentToken={tokens.Current},presc={prescedence},lastPos={lastPosition}{(endDelimiterKind!=null?",endDelim="+endDelimiterKind:"")})");
        };

        AtToken start = null;
        IOperatorDefinition startOp = null;
        ExpressionSyntax operand = null;

        //predicate() - closes over {prescendence}
        Func<IOperatorDefinition,bool> predicate = (IOperatorDefinition _) => _.TokenKind==tokens.Current?.Kind && (_.OperatorPosition==End || Operators.Prescedence(_) >= (startOp != null ? Operators.Prescedence(startOp) : prescedence));
        //bool predicate(IOperatorDefinition _) => _.TokenKind==tokens.Current?.Kind && Operators.Prescedence(_) >= (startOp != null ? Operators.Prescedence(startOp) : prescedence);

        //BEGIN PARSING:

        //No Operators registered : expression cluster
        if (Operators.Count==0)
            return expressionCluster(new AtToken[0],tokens,null,diagnostics);
        
        //End of [post]circumfix expression? (1)
        if (tokens.Current.Kind == endDelimiterKind)
            return null;

        //End operator at beginning? (e.g., ";;")
        var endOps = Operators.Where(_=>_.OperatorPosition==End);   
        var endOp = endOps.FirstOrDefault(predicate);
        if (endOp != null)
            return endOp.CreateExpression(tokens.Consume());
        
        //Start operator? ("@[x;]", "if[(x){ ... }]", etc.)
        var startOps = Operators.Where(_=>_.OperatorPosition==Start);
        startOp = startOps.FirstOrDefault(predicate);
        if (startOp != null)
        {
            start = tokens.Consume();
            startOperator = true;
            if (tokens.Current.Kind == endDelimiterKind)
                return startOp.CreateExpression(start);
        }
        
        //Prefix op ?
        var prefixOps = Operators.Where(_=>_.OperatorPosition==Prefix);
        var prefixOp = prefixOps.FirstOrDefault(predicate);
        if (prefixOp != null)
        {
            var prefixOpToken = tokens.Consume();
            var e = expression(tokens,diagnostics,Operators.Prescedence(prefixOp),tokens.Position,endDelimiterKind,startOperator);
            operand = prefixOp.CreateExpression(prefixOpToken, e);
        }        
        else 
        {
            //Circumfix op?
            var circumfixOps = Operators.Where(_=>_.OperatorPosition==OperatorPosition.Circumfix);
            var circumfixOp =  circumfixOps.FirstOrDefault(predicate);
            if (circumfixOp != null)
                operand = parseCircumfixOp(circumfixOp, tokens, diagnostics,null, startOperator);
            
            //...
            else
            {
                //checks passed-in position from recursive call to prevent stack overflow
                if  (lastPosition != tokens.Position) 
                {
                    operand = expression(tokens,diagnostics,startOp != null ? Operators.Prescedence(startOp) : prescedence,  tokens.Position, endDelimiterKind, startOperator);
                    //Debug.WriteLine($"229: operand = ({operand.GetType().Name}) {operand.Text}");

                    var _opdef = operand.ExpressionSource as IOperatorDefinition;
                }
            
                //same position as before? check expression rules
                else
                {
                    var exprRule = getRule(tokens);
                    if (exprRule != null)
                    {
                        var pos = tokens.Position;

                        operand = exprRule.ParseExpression(tokens);

                        if (operand != null && pos == tokens.Position && operand.Text.Length > 0)
                            tokens.MoveNext();
                    }

                    if (operand == null)
                    {
                        operand = expressionCluster(new[]{tokens.Consume()},null,null,diagnostics); 
                    }
                }

                
                //application expression
                if (   startOp == null 
                    && !tokens.End 
                    && (endDelimiterKind==null || tokens.Current.Kind!=endDelimiterKind) 
                    
                    //HACK: uses check for semi-colon instead of handling (elsewhere) for
                    //      expressions returned from end-position operator
                    && operand?.nodes.Last().AsToken()?.Kind!=TokenKind.SemiColon)
                {
                    var op = Operators.Where(_=>_.OperatorPosition==End
                                              ||_.OperatorPosition==PostCircumfix
                                              ||_.OperatorPosition==Infix)
                                      .FirstOrDefault(predicate);

                    //TODO: ?? CHECK FOR SYNTAX ERRORS...
                    if(op == null)
                    {
                        var e = expression(tokens,diagnostics, prescedence,  tokens.Position, endDelimiterKind,start!=null);
                        operand = applicationExpression(operand,e);
                    }
                }
            }
        }
    

        //End?
        endOp = endOps.FirstOrDefault(predicate);
        if (endOp != null)
            return endOp.CreateExpression(operand != null ? new AtSyntaxNode[]{operand, tokens.Consume()} : new AtSyntaxNode[] {tokens.Consume()} );

        //Postcircumfix?
        var postCircumfixOps = Operators.Where(_=>_.OperatorPosition==PostCircumfix);
        var postCircumfixOp = postCircumfixOps.FirstOrDefault(predicate);
        while (postCircumfixOp != null) //compund postcircumfix expressions
        {
            operand = parseCircumfixOp(postCircumfixOp,tokens,diagnostics,operand, startOperator);
            //Debug.WriteLine($"275: operand = ({operand.GetType().Name}) {operand.Text}");

            postCircumfixOp = postCircumfixOps.FirstOrDefault(predicate);
        }

        //Postfix?
        var postfixOps = Operators.SelectMany(_=>_).Where(_=>_.OperatorPosition==Postfix);
        var postfixOp = postfixOps.FirstOrDefault(predicate);
        if (postfixOp != null)
        {
            var postfixOpToken = tokens.Consume();
            operand = postfixOp.CreateExpression(operand,postfixOpToken);
        }

        //Binary op?
        IOperatorDefinition binaryOp = null;
        foreach(var ops in Operators)
        {
            binaryOp = ops.FirstOrDefault(_=>predicate(_) && _.OperatorPosition==Infix);
            if (binaryOp != null)
            {
               //Same precedence, but not right-associative—deal with this "later"
               if (Operators.Prescedence(binaryOp)==prescedence && binaryOp.Associativity!=Right)
                 break;
               
               var opToken = tokens.Consume();
               var rightOperand = expression(tokens,diagnostics,Operators.Prescedence(binaryOp),tokens.Position,endDelimiterKind,start!=null);
               operand = binaryOp.CreateExpression(operand, opToken, rightOperand);
            }
        }

        //End?
        endOp = endOps.FirstOrDefault(predicate);
        if (endOp != null)
            return endOp.CreateExpression(operand != null ? new AtSyntaxNode[]{operand, tokens.Consume()} : new AtSyntaxNode[] {tokens.Consume()} );

        //________________________________
        Debug.Assert(operand != null);

        var retval = operand;

        if (endOp != null)
            retval = endOp.CreateExpression(new AtSyntaxNode[] { operand, tokens.Consume() });

        else if (start != null)
        {
            retval = startOp.CreateExpression(start, operand);
            return retval; //skip transformation
        }


        //expression transformation
        if (!startOperator)
        {
            var exprTx = getTransform(retval);
            if (exprTx != null)
            {
                var retval2 = exprTx.TransformExpression(retval);

                if (retval2 != null && retval2.Text.Length > 0)
                    return retval2;
            }
        }

        return retval;
    }

    ExpressionSyntax applicationExpression(ExpressionSyntax subj, ExpressionSyntax obj)
    {
       var ae = obj as ApplicationSyntax;
       return ae != null 
                ? Apply(subj,new[]{ae.Subject}.Concat(ae.Arguments).ToArray())
                : Apply(subj,obj);
    }

    ExpressionSyntax parseCircumfixOp(IOperatorDefinition circumfixOp, Limpl.IScanner<AtToken> tokens, List<AtDiagnostic> diagnostics, AtSyntaxNode postCircumfixOperand,bool startOp)
    {
        var startDelimiter = tokens.Consume();
        var _endDelimiterKind = (circumfixOp as ICircumfixOperatorDefinition)?.EndDelimiterKind ?? circumfixOp.TokenKind;
        var list = new List<AtSyntaxNode>();
        if (postCircumfixOperand != null)
            list.Add(postCircumfixOperand);
        list.Add(startDelimiter);
        while(!tokens.End && tokens.Current.Kind != _endDelimiterKind)
        {
            var e = expression(tokens,diagnostics,0,tokens.Position,_endDelimiterKind,startOp);
            if (e != null)
                list.Add(e);
        } 

        if (tokens.Current?.Kind == _endDelimiterKind)
            list.Add(tokens.Consume()); 
        else
            diagnostics.Add(AtDiagnostic.Create(DiagnosticIds.UnexpectedToken,tokens.Current,DiagnosticSeverity.Error,"Expected "+_endDelimiterKind));

        var retval = circumfixOp.CreateExpression(list.ToArray());
        return retval;
    }

    IExpressionTransformation getTransform(ExpressionSyntax e)
    {
        return Transformations.Matches(e).FirstOrDefault(); 
    }

    IExpressionRule getRule(Limpl.IScanner<AtToken> tokens)
    {
        int k = -1;
        var anyMatch = false;
        ExpressionRuleList lastMatches = ExpressionRules, matches;

        if (ExpressionRules.Count > 0)
        {
            k = -1;

            while((matches = lastMatches.Matches(tokens,++k)).Count>0)
            {
                lastMatches = matches;
                anyMatch = true;

                if (tokens.End)
                    break;
            }

            if (anyMatch && lastMatches?.Count > 0)
                return lastMatches[0];
        }    

        return null;    
    }

    ExpressionClusterSyntax expressionCluster(IEnumerable<AtToken> tokens1, Limpl.IScanner<AtToken> tokens2, IExpressionSource expSrc, List<AtDiagnostic> diagnostics)
    {
        var nodes = new List<AtSyntaxNode>(tokens1);
        while (!tokens2?.End ?? false)
            nodes.Add(tokens2.Consume());
        return new ExpressionClusterSyntax(nodes,expSrc,diagnostics);
    }

    void IDisposable.Dispose()
    {
            
    }
}
}