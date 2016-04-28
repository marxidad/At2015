﻿//CSharpScript (csi.exe)

#r "bin\Debug\At.exe"

using System.IO;
using System.Linq;
using At;


var x = At.AtSyntaxTree.ParseText("@variable : y; @y<>; @class<>  : y {@P<>;}");
var root = x.GetRoot();
var c = AtCompilation.Create(new[] {x});
var s = new MemoryStream();
var r = c.Emit(s);
var srcs = r.ConvertedSources().ToArray();
var src = srcs[0];

s.Seek(0,SeekOrigin.Begin);
var a  = System.Reflection.Assembly.Load(s.ToArray());
var ts = a.GetTypes();

