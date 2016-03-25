﻿using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace At 
{
public class AtEmitResult 
{
    bool success = false;
    IEnumerable<string> convertedSources;

    public AtEmitResult(bool success,ImmutableArray<Diagnostic> diagnostics,IEnumerable<string> convertedSources)
    {
        this.success     = success;
        this.Diagnostics = diagnostics;
        this.convertedSources = convertedSources;
    }

    public bool Success
    {
        get { return this.success; }
    }

    public ImmutableArray<Diagnostic> Diagnostics {get;}

    public IEnumerable<string>  ConvertedSources() {return convertedSources;}

}
}