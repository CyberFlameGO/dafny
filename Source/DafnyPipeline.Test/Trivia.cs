using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.Contracts;
using System.IO;
using System.Text.RegularExpressions;
using Bpl = Microsoft.Boogie;
using BplParser = Microsoft.Boogie.Parser;
using Microsoft.Dafny;
using Xunit;

namespace DafnyPipeline.Test {
  [Collection("Singleton Test Collection - Trivia")]
  public class Trivia {
    enum Newlines { LF, CR, CRLF };

    private Newlines currentNewlines;

    [Fact]
    public void TriviaSplitWorksOnLinuxMacAndWindows() {
      ErrorReporter reporter = new ConsoleErrorReporter();
      var options = DafnyOptions.Create();
      DafnyOptions.Install(options);
      foreach (Newlines newLinesType in Enum.GetValues(typeof(Newlines))) {
        currentNewlines = newLinesType;
        var programString = @"
// Comment before
module Test // Module docstring
{}

/** Trait docstring */
trait Trait1 { }

// Just a comment
trait Trait2 extends Trait1
// Trait docstring
{ }
// This is attached to trait2

// This is attached to n
type n = x: int | x % 2 == 0
// This is attached to n as well

// Just a comment
class Class1 extends Trait1
// Class docstring
{ }
// This is attached to the class

// Comment attached to c
const c := 2;
// Docstring attached to c

// This is attached to f
function f(): int
// This is f docstring
ensures true
{ 1 }

/** This is the docstring */
function g(): int
// This is not the docstring
ensures true
{ 1 }

// Just a regular comment
method m() returns (r: int)
// This is the docstring
ensures true
{ assert true; }
";
        programString = AdjustNewlines(programString);

        ModuleDecl module = new LiteralModuleDecl(new DefaultModuleDecl(), null);
        Microsoft.Dafny.Type.ResetScopes();
        BuiltIns builtIns = new BuiltIns();
        Parser.Parse(programString, "virtual", "virtual", module, builtIns, reporter);
        var dafnyProgram = new Program("programName", module, builtIns, reporter);
        Assert.Equal(0, reporter.ErrorCount);
        Assert.Equal(6, dafnyProgram.DefaultModuleDef.TopLevelDecls.Count);
        var moduleTest = dafnyProgram.DefaultModuleDef.TopLevelDecls[0] as LiteralModuleDecl;
        var trait1 = dafnyProgram.DefaultModuleDef.TopLevelDecls[1];
        var trait2 = dafnyProgram.DefaultModuleDef.TopLevelDecls[2];
        var subsetType = dafnyProgram.DefaultModuleDef.TopLevelDecls[3];
        var class1 = dafnyProgram.DefaultModuleDef.TopLevelDecls[4] as ClassDecl;
        var defaultClass = dafnyProgram.DefaultModuleDef.TopLevelDecls[5] as ClassDecl;
        Assert.NotNull(moduleTest);
        Assert.NotNull(class1);
        Assert.NotNull(defaultClass);
        Assert.Equal(4, defaultClass.Members.Count);
        var c = defaultClass.Members[0];
        var f = defaultClass.Members[1];
        var g = defaultClass.Members[2];
        var m = defaultClass.Members[3];
        Assert.NotNull(trait1.StartToken.Next);
        Assert.Equal("Trait1", trait1.StartToken.Next.val);

        AssertTrivia(trait1, "\n/** Trait docstring */\n", " ");
        AssertTrivia(moduleTest, "\n// Comment before\n", " // Module docstring\n");
        AssertTrivia(trait2, "\n// Just a comment\n", "\n// Trait docstring\n");
        AssertTrivia(subsetType, "\n// This is attached to n\n", "\n// This is attached to n as well\n");
        AssertTrivia(class1, "\n// Just a comment\n", "\n// Class docstring\n");
        AssertTrivia(c, "\n// Comment attached to c\n", "\n// Docstring attached to c\n");
        AssertTrivia(f, "\n// This is attached to f\n", "\n// This is f docstring\n");
        AssertTrivia(g, "\n/** This is the docstring */\n", "\n// This is not the docstring\n");
        AssertTrivia(m, "\n// Just a regular comment\n", "\n// This is the docstring\n");
      }
    }

    private string AdjustNewlines(string programString) {
      return currentNewlines switch {
        Newlines.LF => new Regex("\r?\n").Replace(programString, "\n"),
        Newlines.CR => new Regex("\r?\n").Replace(programString, "\r"),
        _ => new Regex("\r?\n").Replace(programString, "\r\n")
      };
    }

    private void AssertTrivia(TopLevelDecl topLevelDecl, string triviaBefore, string triviaDoc) {
      Assert.Equal(AdjustNewlines(triviaBefore), topLevelDecl.StartToken.LeadingTrivia);
      Assert.Equal(AdjustNewlines(triviaDoc), topLevelDecl.TokenWithTrailingDocString.TrailingTrivia);
    }

    private void AssertTrivia(MemberDecl topLevelDecl, string triviaBefore, string triviaDoc) {
      Assert.Equal(AdjustNewlines(triviaBefore), topLevelDecl.StartToken.LeadingTrivia);
      Assert.Equal(AdjustNewlines(triviaDoc), topLevelDecl.TokenWithTrailingDocString.TrailingTrivia);
    }
  }
}
