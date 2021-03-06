﻿using System;
using System.Collections.Generic;
using static At.SyntaxFactory;

namespace At.Syntax
{
public class TypeDeclarationSyntax: DeclarationSyntax, IHasIdentifier
{

    internal TypeDeclarationSyntax 
    (
        AtToken atSymbol, 
        AtToken identifier, 
        ListSyntax<ParameterSyntax> typeParameterList, 
        ListSyntax<NameSyntax> baseList,
        IEnumerable<DeclarationSyntax> members,
        IExpressionSource expDef,
        IEnumerable<AtSyntaxNode> nodes,
        IEnumerable<AtDiagnostic> diagnostics) 
        
        : base(atSymbol, new NameSyntax(identifier), nodes, expDef, diagnostics) {

        Identifier = identifier;
        TypeParameters = typeParameterList ?? List<ParameterSyntax>("<",">");
        BaseTypes = baseList;
        Members = new AtSyntaxList<DeclarationSyntax>(this,members);
    }

    public AtToken Identifier {get;}
    public ListSyntax<NameSyntax> BaseTypes {get;}
    public ListSyntax<ParameterSyntax> TypeParameters {get;}
    public AtSyntaxList<DeclarationSyntax> Members {get;}

    public override TResult Accept<TResult>(AtSyntaxVisitor<TResult> visitor)
    {
        return visitor.VisitTypeDeclaration(this);
    }
}
}