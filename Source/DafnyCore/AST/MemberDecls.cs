using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Numerics;
using System.Linq;

namespace Microsoft.Dafny;

public abstract class MemberDecl : Declaration {
  public abstract string WhatKind { get; }
  public virtual string WhatKindMentionGhost => (IsGhost ? "ghost " : "") + WhatKind;
  public readonly bool HasStaticKeyword;
  public virtual bool IsStatic {
    get {
      return HasStaticKeyword || (EnclosingClass is ClassDecl && ((ClassDecl)EnclosingClass).IsDefaultClass);
    }
  }
  protected readonly bool isGhost;
  public bool IsGhost { get { return isGhost; } }

  /// <summary>
  /// The term "instance independent" can be confusing. It means that the constant does not get its value in
  /// a constructor. (But the RHS of the const's declaration may mention "this".)
  /// </summary>
  public bool IsInstanceIndependentConstant => this is ConstantField cf && cf.Rhs != null;

  public TopLevelDecl EnclosingClass;  // filled in during resolution
  [FilledInDuringResolution] public MemberDecl RefinementBase;  // filled in during the pre-resolution refinement transformation; null if the member is new here
  [FilledInDuringResolution] public MemberDecl OverriddenMember;  // non-null if the member overrides a member in a parent trait
  public virtual bool IsOverrideThatAddsBody => OverriddenMember != null;

  /// <summary>
  /// Returns "true" if "this" is a (possibly transitive) override of "possiblyOverriddenMember".
  /// </summary>
  public bool Overrides(MemberDecl possiblyOverriddenMember) {
    Contract.Requires(possiblyOverriddenMember != null);
    for (var th = this; th != null; th = th.OverriddenMember) {
      if (th == possiblyOverriddenMember) {
        return true;
      }
    }
    return false;
  }

  public MemberDecl(IToken tok, string name, bool hasStaticKeyword, bool isGhost, Attributes attributes, bool isRefining)
    : base(tok, name, attributes, isRefining) {
    Contract.Requires(tok != null);
    Contract.Requires(name != null);
    HasStaticKeyword = hasStaticKeyword;
    this.isGhost = isGhost;
  }
  /// <summary>
  /// Returns className+"."+memberName.  Available only after resolution.
  /// </summary>
  public virtual string FullDafnyName {
    get {
      Contract.Requires(EnclosingClass != null);
      Contract.Ensures(Contract.Result<string>() != null);
      string n = EnclosingClass.FullDafnyName;
      return (n.Length == 0 ? n : (n + ".")) + Name;
    }
  }
  public virtual string FullName {
    get {
      Contract.Requires(EnclosingClass != null);
      Contract.Ensures(Contract.Result<string>() != null);

      return EnclosingClass.FullName + "." + Name;
    }
  }

  public override string SanitizedName =>
    (Name == EnclosingClass.Name ? "_" : "") + base.SanitizedName;

  public override string CompileName =>
    (Name == EnclosingClass.Name ? "_" : "") + base.CompileName;

  public virtual string FullSanitizedName {
    get {
      Contract.Requires(EnclosingClass != null);
      Contract.Ensures(Contract.Result<string>() != null);

      if (Name == "requires") {
        return Translator.Requires(((ArrowTypeDecl)EnclosingClass).Arity);
      } else if (Name == "reads") {
        return Translator.Reads(((ArrowTypeDecl)EnclosingClass).Arity);
      } else {
        return EnclosingClass.FullSanitizedName + "." + SanitizedName;
      }
    }
  }

  public virtual IEnumerable<Expression> SubExpressions => Enumerable.Empty<Expression>();
}

public class Field : MemberDecl {
  public override string WhatKind => "field";
  public readonly bool IsMutable;  // says whether or not the field can ever change values
  public readonly bool IsUserMutable;  // says whether or not code is allowed to assign to the field (IsUserMutable implies IsMutable)
  public readonly Type Type;
  [ContractInvariantMethod]
  void ObjectInvariant() {
    Contract.Invariant(Type != null);
    Contract.Invariant(!IsUserMutable || IsMutable);  // IsUserMutable ==> IsMutable
  }

  public override IEnumerable<INode> Children => Type.Nodes;

  public Field(IToken tok, string name, bool isGhost, Type type, Attributes attributes)
    : this(tok, name, false, isGhost, true, true, type, attributes) {
    Contract.Requires(tok != null);
    Contract.Requires(name != null);
    Contract.Requires(type != null);
  }

  public Field(IToken tok, string name, bool hasStaticKeyword, bool isGhost, bool isMutable, bool isUserMutable, Type type, Attributes attributes)
    : base(tok, name, hasStaticKeyword, isGhost, attributes, false) {
    Contract.Requires(tok != null);
    Contract.Requires(name != null);
    Contract.Requires(type != null);
    Contract.Requires(!isUserMutable || isMutable);
    IsMutable = isMutable;
    IsUserMutable = isUserMutable;
    Type = type;
  }
}

public class SpecialFunction : Function, ICodeContext, ICallable {
  readonly ModuleDefinition Module;
  public SpecialFunction(IToken tok, string name, ModuleDefinition module, bool hasStaticKeyword, bool isGhost,
    List<TypeParameter> typeArgs, List<Formal> formals, Type resultType,
    List<AttributedExpression> req, List<FrameExpression> reads, List<AttributedExpression> ens, Specification<Expression> decreases,
    Expression body, Attributes attributes, IToken signatureEllipsis)
    : base(tok, name, hasStaticKeyword, isGhost, typeArgs, formals, null, resultType, req, reads, ens, decreases, body, null, null, attributes, signatureEllipsis) {
    Module = module;
  }
  ModuleDefinition ICodeContext.EnclosingModule { get { return this.Module; } }
  string ICallable.NameRelativeToModule { get { return Name; } }
}

public class SpecialField : Field {
  public enum ID {
    UseIdParam,  // IdParam is a string
    ArrayLength,  // IdParam is null for .Length; IdParam is an int "x" for GetLength(x)
    ArrayLengthInt,  // same as ArrayLength, but produces int instead of BigInteger
    Floor,
    IsLimit,
    IsSucc,
    Offset,
    IsNat,
    Keys,
    Values,
    Items,
    Reads,
    Modifies,
    New,
  }
  public readonly ID SpecialId;
  public readonly object IdParam;
  public SpecialField(IToken tok, string name, ID specialId, object idParam,
    bool isGhost, bool isMutable, bool isUserMutable, Type type, Attributes attributes)
    : this(tok, name, specialId, idParam, false, isGhost, isMutable, isUserMutable, type, attributes) {
    Contract.Requires(tok != null);
    Contract.Requires(name != null);
    Contract.Requires(!isUserMutable || isMutable);
    Contract.Requires(type != null);
  }

  public SpecialField(IToken tok, string name, ID specialId, object idParam,
    bool hasStaticKeyword, bool isGhost, bool isMutable, bool isUserMutable, Type type, Attributes attributes)
    : base(tok, name, hasStaticKeyword, isGhost, isMutable, isUserMutable, type, attributes) {
    Contract.Requires(tok != null);
    Contract.Requires(name != null);
    Contract.Requires(!isUserMutable || isMutable);
    Contract.Requires(type != null);

    SpecialId = specialId;
    IdParam = idParam;
  }

  public override string FullName {
    get {
      Contract.Ensures(Contract.Result<string>() != null);
      return EnclosingClass != null ? EnclosingClass.FullName + "." + Name : Name;
    }
  }

  public override string FullSanitizedName { // Override beacuse EnclosingClass may be null
    get {
      Contract.Ensures(Contract.Result<string>() != null);
      return EnclosingClass != null ? EnclosingClass.FullSanitizedName + "." + SanitizedName : SanitizedName;
    }
  }

  public override string CompileName {
    get {
      Contract.Ensures(Contract.Result<string>() != null);
      return EnclosingClass != null ? base.CompileName : Name;
    }
  }
}

public class DatatypeDiscriminator : SpecialField {
  public override string WhatKind {
    get { return "discriminator"; }
  }

  public DatatypeDiscriminator(IToken tok, string name, ID specialId, object idParam, bool isGhost, Type type, Attributes attributes)
    : base(tok, name, specialId, idParam, isGhost, false, false, type, attributes) {
  }
}

public class DatatypeDestructor : SpecialField {
  public readonly List<DatatypeCtor> EnclosingCtors = new List<DatatypeCtor>();  // is always a nonempty list
  public readonly List<Formal> CorrespondingFormals = new List<Formal>();  // is always a nonempty list
  [ContractInvariantMethod]
  void ObjectInvariant() {
    Contract.Invariant(EnclosingCtors != null);
    Contract.Invariant(CorrespondingFormals != null);
    Contract.Invariant(EnclosingCtors.Count > 0);
    Contract.Invariant(EnclosingCtors.Count == CorrespondingFormals.Count);
  }

  public DatatypeDestructor(IToken tok, DatatypeCtor enclosingCtor, Formal correspondingFormal, string name, string compiledName, bool isGhost, Type type, Attributes attributes)
    : base(tok, name, SpecialField.ID.UseIdParam, compiledName, isGhost, false, false, type, attributes) {
    Contract.Requires(tok != null);
    Contract.Requires(enclosingCtor != null);
    Contract.Requires(correspondingFormal != null);
    Contract.Requires(name != null);
    Contract.Requires(type != null);
    EnclosingCtors.Add(enclosingCtor);  // more enclosing constructors may be added later during resolution
    CorrespondingFormals.Add(correspondingFormal);  // more corresponding formals may be added later during resolution
  }

  /// <summary>
  /// To be called only by the resolver. Called to share this datatype destructor between multiple constructors
  /// of the same datatype.
  /// </summary>
  internal void AddAnotherEnclosingCtor(DatatypeCtor ctor, Formal formal) {
    Contract.Requires(ctor != null);
    Contract.Requires(formal != null);
    EnclosingCtors.Add(ctor);  // more enclosing constructors may be added later during resolution
    CorrespondingFormals.Add(formal);  // more corresponding formals may be added later during resolution
  }

  internal string EnclosingCtorNames(string grammaticalConjunction) {
    Contract.Requires(grammaticalConjunction != null);
    return PrintableCtorNameList(EnclosingCtors, grammaticalConjunction);
  }

  static internal string PrintableCtorNameList(List<DatatypeCtor> ctors, string grammaticalConjunction) {
    Contract.Requires(ctors != null);
    Contract.Requires(grammaticalConjunction != null);
    return Util.PrintableNameList(ctors.ConvertAll(ctor => ctor.Name), grammaticalConjunction);
  }
}

public class ConstantField : SpecialField, ICallable {
  public override string WhatKind => "const field";
  public readonly Expression Rhs;
  public ConstantField(IToken tok, string name, Expression/*?*/ rhs, bool hasStaticKeyword, bool isGhost, Type type, Attributes attributes)
    : base(tok, name, SpecialField.ID.UseIdParam, NonglobalVariable.SanitizeName(name), hasStaticKeyword, isGhost, false, false, type, attributes) {
    Contract.Requires(tok != null);
    Contract.Requires(name != null);
    Contract.Requires(type != null);
    this.Rhs = rhs;
  }

  public override bool CanBeRevealed() {
    return true;
  }

  //
  public new bool IsGhost { get { return this.isGhost; } }
  public List<TypeParameter> TypeArgs { get { return new List<TypeParameter>(); } }
  public List<Formal> Ins { get { return new List<Formal>(); } }
  public ModuleDefinition EnclosingModule { get { return this.EnclosingClass.EnclosingModuleDefinition; } }
  public bool MustReverify { get { return false; } }
  public bool AllowsNontermination { get { throw new cce.UnreachableException(); } }
  public IToken Tok { get { return tok; } }
  public string NameRelativeToModule {
    get {
      if (EnclosingClass is DefaultClassDecl) {
        return Name;
      } else {
        return EnclosingClass.Name + "." + Name;
      }
    }
  }
  public Specification<Expression> Decreases { get { throw new cce.UnreachableException(); } }
  public bool InferredDecreases {
    get { throw new cce.UnreachableException(); }
    set { throw new cce.UnreachableException(); }
  }
  public bool AllowsAllocation => true;

  public override IEnumerable<INode> Children => base.Children.Concat(new[] { Rhs }.Where(x => x != null));
}

public class Function : MemberDecl, TypeParameter.ParentType, ICallable {
  public override string WhatKind => "function";

  public string FunctionDeclarationKeywords {
    get {
      string k;
      if (this is TwoStateFunction || this is ExtremePredicate || this.ByMethodBody != null) {
        k = WhatKind;
      } else if (this is PrefixPredicate) {
        k = "predicate";
      } else if (DafnyOptions.O.FunctionSyntax == DafnyOptions.FunctionSyntaxOptions.ExperimentalPredicateAlwaysGhost &&
                 (this is Predicate || !IsGhost)) {
        k = WhatKind;
      } else if (DafnyOptions.O.FunctionSyntax != DafnyOptions.FunctionSyntaxOptions.Version4 && !IsGhost) {
        k = WhatKind + " method";
      } else if (DafnyOptions.O.FunctionSyntax != DafnyOptions.FunctionSyntaxOptions.Version3 && IsGhost) {
        k = "ghost " + WhatKind;
      } else {
        k = WhatKind;
      }

      return HasStaticKeyword ? "static " + k : k;
    }
  }

  public override bool CanBeRevealed() {
    return true;
  }

  [FilledInDuringResolution] public bool IsRecursive;

  [FilledInDuringResolution]
  public TailStatus
    TailRecursion =
      TailStatus.NotTailRecursive; // NotTailRecursive = no tail recursion; TriviallyTailRecursive is never used here

  public bool IsTailRecursive => TailRecursion != TailStatus.NotTailRecursive;
  public bool IsAccumulatorTailRecursive => IsTailRecursive && TailRecursion != Function.TailStatus.TailRecursive;
  [FilledInDuringResolution] public bool IsFueled; // if anyone tries to adjust this function's fuel
  public readonly List<TypeParameter> TypeArgs;
  public readonly List<Formal> Formals;
  public readonly Formal Result;
  public readonly Type ResultType;
  public readonly List<AttributedExpression> Req;
  public readonly List<FrameExpression> Reads;
  public readonly List<AttributedExpression> Ens;
  public readonly Specification<Expression> Decreases;
  public Expression Body; // an extended expression; Body is readonly after construction, except for any kind of rewrite that may take place around the time of resolution
  public IToken /*?*/ ByMethodTok; // null iff ByMethodBody is null
  public BlockStmt /*?*/ ByMethodBody;
  [FilledInDuringResolution] public Method /*?*/ ByMethodDecl; // if ByMethodBody is non-null
  public bool SignatureIsOmitted => SignatureEllipsis != null; // is "false" for all Function objects that survive into resolution
  public readonly IToken SignatureEllipsis;
  public Function OverriddenFunction;
  public Function Original => OverriddenFunction == null ? this : OverriddenFunction.Original;
  public override bool IsOverrideThatAddsBody => base.IsOverrideThatAddsBody && Body != null;
  public bool AllowsAllocation => true;
  public bool containsQuantifier;

  public bool ContainsQuantifier {
    set { containsQuantifier = value; }
    get { return containsQuantifier; }
  }

  public enum TailStatus {
    TriviallyTailRecursive, // contains no recursive calls (in non-ghost expressions)
    TailRecursive, // all recursive calls (in non-ghost expressions) are tail calls
    NotTailRecursive, // contains some non-ghost recursive call outside of a tail-call position
    // E + F or F + E, where E has no tail call and F is a tail call
    Accumulate_Add,
    AccumulateRight_Sub,
    Accumulate_Mul,
    Accumulate_SetUnion,
    AccumulateRight_SetDifference,
    Accumulate_MultiSetUnion,
    AccumulateRight_MultiSetDifference,
    AccumulateLeft_Concat,
    AccumulateRight_Concat,
  }

  public override IEnumerable<INode> Children => new[] { ByMethodDecl }.Where(x => x != null).
    Concat<INode>(Reads).
    Concat<INode>(Req.Select(e => e.E)).
    Concat(Ens.Select(e => e.E)).
    Concat(Decreases.Expressions).
    Concat(Formals).Concat(ResultType.Nodes).
    Concat(Body == null ? Enumerable.Empty<INode>() : new[] { Body });

  public override IEnumerable<Expression> SubExpressions {
    get {
      foreach (var formal in Formals.Where(f => f.DefaultValue != null)) {
        yield return formal.DefaultValue;
      }
      foreach (var e in Req) {
        yield return e.E;
      }
      foreach (var e in Reads) {
        yield return e.E;
      }
      foreach (var e in Ens) {
        yield return e.E;
      }
      foreach (var e in Decreases.Expressions) {
        yield return e;
      }
      if (Body != null) {
        yield return Body;
      }
    }
  }

  public Type GetMemberType(ArrowTypeDecl atd) {
    Contract.Requires(atd != null);
    Contract.Requires(atd.Arity == Formals.Count);

    // Note, the following returned type can contain type parameters from the function and its enclosing class
    return new ArrowType(tok, atd, Formals.ConvertAll(f => f.Type), ResultType);
  }

  public bool AllowsNontermination {
    get {
      return Contract.Exists(Decreases.Expressions, e => e is WildcardExpr);
    }
  }

  /// <summary>
  /// The "AllCalls" field is used for non-ExtremePredicate, non-PrefixPredicate functions only (so its value should not be relied upon for ExtremePredicate and PrefixPredicate functions).
  /// It records all function calls made by the Function, including calls made in the body as well as in the specification.
  /// The field is filled in during resolution (and used toward the end of resolution, to attach a helpful "decreases" prefix to functions in clusters
  /// with co-recursive calls.
  /// </summary>
  public readonly List<FunctionCallExpr> AllCalls = new List<FunctionCallExpr>();
  public enum CoCallClusterInvolvement {
    None,  // the SCC containing the function does not involve any co-recursive calls
    IsMutuallyRecursiveTarget,  // the SCC contains co-recursive calls, and this function is the target of some non-self recursive call
    CoRecursiveTargetAllTheWay,  // the SCC contains co-recursive calls, and this function is the target only of self-recursive calls and co-recursive calls
  }
  public CoCallClusterInvolvement CoClusterTarget = CoCallClusterInvolvement.None;

  [ContractInvariantMethod]
  void ObjectInvariant() {
    Contract.Invariant(cce.NonNullElements(TypeArgs));
    Contract.Invariant(cce.NonNullElements(Formals));
    Contract.Invariant(ResultType != null);
    Contract.Invariant(cce.NonNullElements(Req));
    Contract.Invariant(cce.NonNullElements(Reads));
    Contract.Invariant(cce.NonNullElements(Ens));
    Contract.Invariant(Decreases != null);
  }

  public Function(IToken tok, string name, bool hasStaticKeyword, bool isGhost,
    List<TypeParameter> typeArgs, List<Formal> formals, Formal result, Type resultType,
    List<AttributedExpression> req, List<FrameExpression> reads, List<AttributedExpression> ens, Specification<Expression> decreases,
    Expression/*?*/ body, IToken/*?*/ byMethodTok, BlockStmt/*?*/ byMethodBody,
    Attributes attributes, IToken/*?*/ signatureEllipsis)
    : base(tok, name, hasStaticKeyword, isGhost, attributes, signatureEllipsis != null) {

    Contract.Requires(tok != null);
    Contract.Requires(name != null);
    Contract.Requires(cce.NonNullElements(typeArgs));
    Contract.Requires(cce.NonNullElements(formals));
    Contract.Requires(resultType != null);
    Contract.Requires(cce.NonNullElements(req));
    Contract.Requires(cce.NonNullElements(reads));
    Contract.Requires(cce.NonNullElements(ens));
    Contract.Requires(decreases != null);
    Contract.Requires(byMethodBody == null || (!isGhost && body != null)); // function-by-method has a ghost expr and non-ghost stmt, but to callers appears like a functiion-method
    this.IsFueled = false;  // Defaults to false.  Only set to true if someone mentions this function in a fuel annotation
    this.TypeArgs = typeArgs;
    this.Formals = formals;
    this.Result = result;
    this.ResultType = result != null ? result.Type : resultType;
    this.Req = req;
    this.Reads = reads;
    this.Ens = ens;
    this.Decreases = decreases;
    this.Body = body;
    this.ByMethodTok = byMethodTok;
    this.ByMethodBody = byMethodBody;
    this.SignatureEllipsis = signatureEllipsis;

    if (attributes != null) {
      List<Expression> args = Attributes.FindExpressions(attributes, "fuel");
      if (args != null) {
        if (args.Count == 1) {
          LiteralExpr literal = args[0] as LiteralExpr;
          if (literal != null && literal.Value is BigInteger) {
            this.IsFueled = true;
          }
        } else if (args.Count == 2) {
          LiteralExpr literalLow = args[0] as LiteralExpr;
          LiteralExpr literalHigh = args[1] as LiteralExpr;

          if (literalLow != null && literalLow.Value is BigInteger && literalHigh != null && literalHigh.Value is BigInteger) {
            this.IsFueled = true;
          }
        }
      }
    }
  }

  bool ICodeContext.IsGhost { get { return this.IsGhost; } }
  List<TypeParameter> ICodeContext.TypeArgs { get { return this.TypeArgs; } }
  List<Formal> ICodeContext.Ins { get { return this.Formals; } }
  IToken ICallable.Tok { get { return this.tok; } }
  string ICallable.NameRelativeToModule {
    get {
      if (EnclosingClass is DefaultClassDecl) {
        return Name;
      } else {
        return EnclosingClass.Name + "." + Name;
      }
    }
  }
  Specification<Expression> ICallable.Decreases { get { return this.Decreases; } }
  bool _inferredDecr;
  bool ICallable.InferredDecreases {
    set { _inferredDecr = value; }
    get { return _inferredDecr; }
  }
  ModuleDefinition ICodeContext.EnclosingModule { get { return this.EnclosingClass.EnclosingModuleDefinition; } }
  bool ICodeContext.MustReverify { get { return false; } }

  [Pure]
  public bool IsFuelAware() { return IsRecursive || IsFueled || (OverriddenFunction != null && OverriddenFunction.IsFuelAware()); }
  public virtual bool ReadsHeap { get { return Reads.Count != 0; } }
}

public class Predicate : Function {
  public override string WhatKind => "predicate";
  public enum BodyOriginKind {
    OriginalOrInherited,  // this predicate definition is new (and the predicate may or may not have a body), or the predicate's body (whether or not it exists) is being inherited unmodified (from the previous refinement--it may be that the inherited body was itself an extension, for example)
    DelayedDefinition,  // this predicate declaration provides, for the first time, a body--the declaration refines a previously declared predicate, but the previous one had no body
    Extension  // this predicate extends the definition of a predicate with a body in a module being refined
  }
  public readonly BodyOriginKind BodyOrigin;
  public Predicate(IToken tok, string name, bool hasStaticKeyword, bool isGhost,
    List<TypeParameter> typeArgs, List<Formal> formals,
    Formal result,
    List<AttributedExpression> req, List<FrameExpression> reads, List<AttributedExpression> ens, Specification<Expression> decreases,
    Expression body, BodyOriginKind bodyOrigin, IToken/*?*/ byMethodTok, BlockStmt/*?*/ byMethodBody, Attributes attributes, IToken signatureEllipsis)
    : base(tok, name, hasStaticKeyword, isGhost, typeArgs, formals, result, Type.Bool, req, reads, ens, decreases, body, byMethodTok, byMethodBody, attributes, signatureEllipsis) {
    Contract.Requires(bodyOrigin == Predicate.BodyOriginKind.OriginalOrInherited || body != null);
    BodyOrigin = bodyOrigin;
  }
}

/// <summary>
/// An PrefixPredicate is the inductive unrolling P# implicitly declared for every extreme predicate P.
/// </summary>
public class PrefixPredicate : Function {
  public override string WhatKind => "prefix predicate";
  public override string WhatKindMentionGhost => WhatKind;
  public readonly Formal K;
  public readonly ExtremePredicate ExtremePred;
  public PrefixPredicate(IToken tok, string name, bool hasStaticKeyword,
    List<TypeParameter> typeArgs, Formal k, List<Formal> formals,
    List<AttributedExpression> req, List<FrameExpression> reads, List<AttributedExpression> ens, Specification<Expression> decreases,
    Expression body, Attributes attributes, ExtremePredicate extremePred)
    : base(tok, name, hasStaticKeyword, true, typeArgs, formals, null, Type.Bool, req, reads, ens, decreases, body, null, null, attributes, null) {
    Contract.Requires(k != null);
    Contract.Requires(extremePred != null);
    Contract.Requires(formals != null && 1 <= formals.Count && formals[0] == k);
    K = k;
    ExtremePred = extremePred;
  }
}

public abstract class ExtremePredicate : Function {
  public override string WhatKindMentionGhost => WhatKind;
  public enum KType { Unspecified, Nat, ORDINAL }
  public readonly KType TypeOfK;
  public bool KNat {
    get {
      return TypeOfK == KType.Nat;
    }
  }
  [FilledInDuringResolution] public readonly List<FunctionCallExpr> Uses = new List<FunctionCallExpr>();  // used by verifier
  [FilledInDuringResolution] public PrefixPredicate PrefixPredicate;  // (name registration)

  public ExtremePredicate(IToken tok, string name, bool hasStaticKeyword, KType typeOfK,
    List<TypeParameter> typeArgs, List<Formal> formals, Formal result,
    List<AttributedExpression> req, List<FrameExpression> reads, List<AttributedExpression> ens,
    Expression body, Attributes attributes, IToken signatureEllipsis)
    : base(tok, name, hasStaticKeyword, true, typeArgs, formals, result, Type.Bool,
      req, reads, ens, new Specification<Expression>(new List<Expression>(), null), body, null, null, attributes, signatureEllipsis) {
    TypeOfK = typeOfK;
  }

  /// <summary>
  /// For the given call P(s), return P#[depth](s).  The resulting expression shares some of the subexpressions
  /// with 'fexp' (that is, what is returned is not necessarily a clone).
  /// </summary>
  public FunctionCallExpr CreatePrefixPredicateCall(FunctionCallExpr fexp, Expression depth) {
    Contract.Requires(fexp != null);
    Contract.Requires(fexp.Function == this);
    Contract.Requires(depth != null);
    Contract.Ensures(Contract.Result<FunctionCallExpr>() != null);

    var args = new List<Expression>() { depth };
    args.AddRange(fexp.Args);
    var prefixPredCall = new FunctionCallExpr(fexp.tok, this.PrefixPredicate.Name, fexp.Receiver, fexp.OpenParen, fexp.CloseParen, args);
    prefixPredCall.Function = this.PrefixPredicate;  // resolve here
    prefixPredCall.TypeApplication_AtEnclosingClass = fexp.TypeApplication_AtEnclosingClass;  // resolve here
    prefixPredCall.TypeApplication_JustFunction = fexp.TypeApplication_JustFunction;  // resolve here
    prefixPredCall.Type = fexp.Type;  // resolve here
    prefixPredCall.CoCall = fexp.CoCall;  // resolve here
    return prefixPredCall;
  }
}

public class LeastPredicate : ExtremePredicate {
  public override string WhatKind => "least predicate";
  public LeastPredicate(IToken tok, string name, bool hasStaticKeyword, KType typeOfK,
    List<TypeParameter> typeArgs, List<Formal> formals, Formal result,
    List<AttributedExpression> req, List<FrameExpression> reads, List<AttributedExpression> ens,
    Expression body, Attributes attributes, IToken signatureEllipsis)
    : base(tok, name, hasStaticKeyword, typeOfK, typeArgs, formals, result,
      req, reads, ens, body, attributes, signatureEllipsis) {
  }
}

public class GreatestPredicate : ExtremePredicate {
  public override string WhatKind => "greatest predicate";
  public GreatestPredicate(IToken tok, string name, bool hasStaticKeyword, KType typeOfK,
    List<TypeParameter> typeArgs, List<Formal> formals, Formal result,
    List<AttributedExpression> req, List<FrameExpression> reads, List<AttributedExpression> ens,
    Expression body, Attributes attributes, IToken signatureEllipsis)
    : base(tok, name, hasStaticKeyword, typeOfK, typeArgs, formals, result,
      req, reads, ens, body, attributes, signatureEllipsis) {
  }
}

public class TwoStateFunction : Function {
  public override string WhatKind => "twostate function";
  public override string WhatKindMentionGhost => WhatKind;
  public TwoStateFunction(IToken tok, string name, bool hasStaticKeyword,
    List<TypeParameter> typeArgs, List<Formal> formals, Formal result, Type resultType,
    List<AttributedExpression> req, List<FrameExpression> reads, List<AttributedExpression> ens, Specification<Expression> decreases,
    Expression body, Attributes attributes, IToken signatureEllipsis)
    : base(tok, name, hasStaticKeyword, true, typeArgs, formals, result, resultType, req, reads, ens, decreases, body, null, null, attributes, signatureEllipsis) {
    Contract.Requires(tok != null);
    Contract.Requires(name != null);
    Contract.Requires(typeArgs != null);
    Contract.Requires(formals != null);
    Contract.Requires(resultType != null);
    Contract.Requires(req != null);
    Contract.Requires(reads != null);
    Contract.Requires(ens != null);
    Contract.Requires(decreases != null);
  }
  public override bool ReadsHeap { get { return true; } }
}

public class TwoStatePredicate : TwoStateFunction {
  public override string WhatKind => "twostate predicate";
  public TwoStatePredicate(IToken tok, string name, bool hasStaticKeyword,
    List<TypeParameter> typeArgs, List<Formal> formals, Formal result,
    List<AttributedExpression> req, List<FrameExpression> reads, List<AttributedExpression> ens, Specification<Expression> decreases,
    Expression body, Attributes attributes, IToken signatureEllipsis)
    : base(tok, name, hasStaticKeyword, typeArgs, formals, result, Type.Bool, req, reads, ens, decreases, body, attributes, signatureEllipsis) {
    Contract.Requires(tok != null);
    Contract.Requires(name != null);
    Contract.Requires(typeArgs != null);
    Contract.Requires(formals != null);
    Contract.Requires(req != null);
    Contract.Requires(reads != null);
    Contract.Requires(ens != null);
    Contract.Requires(decreases != null);
  }
}

public class Method : MemberDecl, TypeParameter.ParentType, IMethodCodeContext {
  public override IEnumerable<INode> Children => (Body?.SubStatements ?? Enumerable.Empty<INode>()).Concat<INode>(Ins).Concat(Outs).Concat(TypeArgs).
    Concat(Req.Select(r => r.E)).Concat(Ens.Select(r => r.E)).Concat(Mod.Expressions).Concat(Decreases.Expressions).
    Concat(Attributes?.Args ?? Enumerable.Empty<INode>());

  public override string WhatKind => "method";
  public bool SignatureIsOmitted { get { return SignatureEllipsis != null; } }
  public readonly IToken SignatureEllipsis;
  public readonly bool IsByMethod;
  public bool MustReverify;
  public bool IsEntryPoint = false;
  public readonly List<TypeParameter> TypeArgs;
  public readonly List<Formal> Ins;
  public readonly List<Formal> Outs;
  public readonly List<AttributedExpression> Req;
  public readonly Specification<FrameExpression> Mod;
  public readonly List<AttributedExpression> Ens;
  public readonly Specification<Expression> Decreases;
  private BlockStmt methodBody;  // Body is readonly after construction, except for any kind of rewrite that may take place around the time of resolution (note that "methodBody" is a "DividedBlockStmt" for any "Method" that is a "Constructor")
  [FilledInDuringResolution] public bool IsRecursive;
  [FilledInDuringResolution] public bool IsTailRecursive;
  public readonly ISet<IVariable> AssignedAssumptionVariables = new HashSet<IVariable>();
  public Method OverriddenMethod;
  public Method Original => OverriddenMethod == null ? this : OverriddenMethod.Original;
  public override bool IsOverrideThatAddsBody => base.IsOverrideThatAddsBody && Body != null;
  private static BlockStmt emptyBody = new BlockStmt(Token.NoToken, Token.NoToken, new List<Statement>());

  public override IEnumerable<Expression> SubExpressions {
    get {
      foreach (var formal in Ins.Where(f => f.DefaultValue != null)) {
        yield return formal.DefaultValue;
      }
      foreach (var e in Req) {
        yield return e.E;
      }
      foreach (var e in Mod.Expressions) {
        yield return e.E;
      }
      foreach (var e in Ens) {
        yield return e.E;
      }
      foreach (var e in Decreases.Expressions) {
        yield return e;
      }
    }
  }

  [ContractInvariantMethod]
  void ObjectInvariant() {
    Contract.Invariant(cce.NonNullElements(TypeArgs));
    Contract.Invariant(cce.NonNullElements(Ins));
    Contract.Invariant(cce.NonNullElements(Outs));
    Contract.Invariant(cce.NonNullElements(Req));
    Contract.Invariant(Mod != null);
    Contract.Invariant(cce.NonNullElements(Ens));
    Contract.Invariant(Decreases != null);
  }

  public Method(IToken tok, string name,
    bool hasStaticKeyword, bool isGhost,
    [Captured] List<TypeParameter> typeArgs,
    [Captured] List<Formal> ins, [Captured] List<Formal> outs,
    [Captured] List<AttributedExpression> req, [Captured] Specification<FrameExpression> mod,
    [Captured] List<AttributedExpression> ens,
    [Captured] Specification<Expression> decreases,
    [Captured] BlockStmt body,
    Attributes attributes, IToken signatureEllipsis, bool isByMethod = false)
    : base(tok, name, hasStaticKeyword, isGhost, attributes, signatureEllipsis != null) {
    Contract.Requires(tok != null);
    Contract.Requires(name != null);
    Contract.Requires(cce.NonNullElements(typeArgs));
    Contract.Requires(cce.NonNullElements(ins));
    Contract.Requires(cce.NonNullElements(outs));
    Contract.Requires(cce.NonNullElements(req));
    Contract.Requires(mod != null);
    Contract.Requires(cce.NonNullElements(ens));
    Contract.Requires(decreases != null);
    this.TypeArgs = typeArgs;
    this.Ins = ins;
    this.Outs = outs;
    this.Req = req;
    this.Mod = mod;
    this.Ens = ens;
    this.Decreases = decreases;
    this.methodBody = body;
    this.SignatureEllipsis = signatureEllipsis;
    this.IsByMethod = isByMethod;
    MustReverify = false;
  }

  bool ICodeContext.IsGhost { get { return this.IsGhost; } }
  List<TypeParameter> ICodeContext.TypeArgs { get { return this.TypeArgs; } }
  List<Formal> ICodeContext.Ins { get { return this.Ins; } }
  List<Formal> IMethodCodeContext.Outs { get { return this.Outs; } }
  Specification<FrameExpression> IMethodCodeContext.Modifies { get { return Mod; } }
  IToken ICallable.Tok { get { return this.tok; } }
  string ICallable.NameRelativeToModule {
    get {
      if (EnclosingClass is DefaultClassDecl) {
        return Name;
      } else {
        return EnclosingClass.Name + "." + Name;
      }
    }
  }
  Specification<Expression> ICallable.Decreases { get { return this.Decreases; } }
  bool _inferredDecr;
  bool ICallable.InferredDecreases {
    set { _inferredDecr = value; }
    get { return _inferredDecr; }
  }

  public virtual bool AllowsAllocation => true;

  ModuleDefinition ICodeContext.EnclosingModule {
    get {
      Contract.Assert(this.EnclosingClass != null);  // this getter is supposed to be called only after signature-resolution is complete
      return this.EnclosingClass.EnclosingModuleDefinition;
    }
  }
  bool ICodeContext.MustReverify { get { return this.MustReverify; } }
  public bool AllowsNontermination {
    get {
      return Contract.Exists(Decreases.Expressions, e => e is WildcardExpr);
    }
  }

  public override string CompileName {
    get {
      var nm = base.CompileName;
      if (nm == Dafny.Compilers.SinglePassCompiler.DefaultNameMain && IsStatic && !IsEntryPoint) {
        // for a static method that is named "Main" but is not a legal "Main" method,
        // change its name.
        nm = EnclosingClass.Name + "_" + nm;
      }
      return nm;
    }
  }

  public BlockStmt Body {
    get {
      // Lemma from included files do not need to be resolved and translated
      // so we return emptyBody. This is to speed up resolver and translator.
      if (methodBody != null && IsLemmaLike && this.tok is IncludeToken && !DafnyOptions.O.VerifyAllModules) {
        return Method.emptyBody;
      } else {
        return methodBody;
      }
    }
    set {
      methodBody = value;
    }
  }

  public bool IsLemmaLike => this is Lemma || this is TwoStateLemma || this is ExtremeLemma || this is PrefixLemma;

  public BlockStmt BodyForRefinement {
    // For refinement, we still need to merge in the body
    // a lemma that is in the refinement base that is defined
    // in a include file.
    get {
      return methodBody;
    }
  }
}

public class Lemma : Method {
  public override string WhatKind => "lemma";
  public override string WhatKindMentionGhost => WhatKind;
  public Lemma(IToken tok, string name,
    bool hasStaticKeyword,
    [Captured] List<TypeParameter> typeArgs,
    [Captured] List<Formal> ins, [Captured] List<Formal> outs,
    [Captured] List<AttributedExpression> req, [Captured] Specification<FrameExpression> mod,
    [Captured] List<AttributedExpression> ens,
    [Captured] Specification<Expression> decreases,
    [Captured] BlockStmt body,
    Attributes attributes, IToken signatureEllipsis)
    : base(tok, name, hasStaticKeyword, true, typeArgs, ins, outs, req, mod, ens, decreases, body, attributes, signatureEllipsis) {
  }

  public override bool AllowsAllocation => false;
}

public class TwoStateLemma : Method {
  public override string WhatKind => "twostate lemma";
  public override string WhatKindMentionGhost => WhatKind;

  public TwoStateLemma(IToken tok, string name,
    bool hasStaticKeyword,
    [Captured] List<TypeParameter> typeArgs,
    [Captured] List<Formal> ins, [Captured] List<Formal> outs,
    [Captured] List<AttributedExpression> req,
    [Captured] Specification<FrameExpression> mod,
    [Captured] List<AttributedExpression> ens,
    [Captured] Specification<Expression> decreases,
    [Captured] BlockStmt body,
    Attributes attributes, IToken signatureEllipsis)
    : base(tok, name, hasStaticKeyword, true, typeArgs, ins, outs, req, mod, ens, decreases, body, attributes, signatureEllipsis) {
    Contract.Requires(tok != null);
    Contract.Requires(name != null);
    Contract.Requires(typeArgs != null);
    Contract.Requires(ins != null);
    Contract.Requires(outs != null);
    Contract.Requires(req != null);
    Contract.Requires(mod != null);
    Contract.Requires(ens != null);
    Contract.Requires(decreases != null);
  }

  public override bool AllowsAllocation => false;
}

public class Constructor : Method {
  public override string WhatKind => "constructor";
  [ContractInvariantMethod]
  void ObjectInvariant() {
    Contract.Invariant(Body == null || Body is DividedBlockStmt);
  }
  public List<Statement> BodyInit {  // first part of Body's statements
    get {
      if (Body == null) {
        return null;
      } else {
        return ((DividedBlockStmt)Body).BodyInit;
      }
    }
  }
  public List<Statement> BodyProper {  // second part of Body's statements
    get {
      if (Body == null) {
        return null;
      } else {
        return ((DividedBlockStmt)Body).BodyProper;
      }
    }
  }
  public Constructor(IToken tok, string name,
    bool isGhost,
    List<TypeParameter> typeArgs,
    List<Formal> ins,
    List<AttributedExpression> req, [Captured] Specification<FrameExpression> mod,
    List<AttributedExpression> ens,
    Specification<Expression> decreases,
    DividedBlockStmt body,
    Attributes attributes, IToken signatureEllipsis)
    : base(tok, name, false, isGhost, typeArgs, ins, new List<Formal>(), req, mod, ens, decreases, body, attributes, signatureEllipsis) {
    Contract.Requires(tok != null);
    Contract.Requires(name != null);
    Contract.Requires(cce.NonNullElements(typeArgs));
    Contract.Requires(cce.NonNullElements(ins));
    Contract.Requires(cce.NonNullElements(req));
    Contract.Requires(mod != null);
    Contract.Requires(cce.NonNullElements(ens));
    Contract.Requires(decreases != null);
  }

  public bool HasName {
    get {
      return Name != "_ctor";
    }
  }
}

/// <summary>
/// A PrefixLemma is the inductive unrolling M# implicitly declared for every extreme lemma M.
/// </summary>
public class PrefixLemma : Method {
  public override string WhatKind => "prefix lemma";
  public override string WhatKindMentionGhost => WhatKind;

  public readonly Formal K;
  public readonly ExtremeLemma ExtremeLemma;
  public PrefixLemma(IToken tok, string name, bool hasStaticKeyword,
    List<TypeParameter> typeArgs, Formal k, List<Formal> ins, List<Formal> outs,
    List<AttributedExpression> req, Specification<FrameExpression> mod, List<AttributedExpression> ens, Specification<Expression> decreases,
    BlockStmt body, Attributes attributes, ExtremeLemma extremeLemma)
    : base(tok, name, hasStaticKeyword, true, typeArgs, ins, outs, req, mod, ens, decreases, body, attributes, null) {
    Contract.Requires(k != null);
    Contract.Requires(ins != null && 1 <= ins.Count && ins[0] == k);
    Contract.Requires(extremeLemma != null);
    K = k;
    ExtremeLemma = extremeLemma;
  }

  public override bool AllowsAllocation => false;
}

public abstract class ExtremeLemma : Method {
  public override string WhatKindMentionGhost => WhatKind;
  public readonly ExtremePredicate.KType TypeOfK;
  public bool KNat {
    get {
      return TypeOfK == ExtremePredicate.KType.Nat;
    }
  }
  [FilledInDuringResolution] public PrefixLemma PrefixLemma;  // (name registration)

  public ExtremeLemma(IToken tok, string name,
    bool hasStaticKeyword, ExtremePredicate.KType typeOfK,
    List<TypeParameter> typeArgs,
    List<Formal> ins, [Captured] List<Formal> outs,
    List<AttributedExpression> req, [Captured] Specification<FrameExpression> mod,
    List<AttributedExpression> ens,
    Specification<Expression> decreases,
    BlockStmt body,
    Attributes attributes, IToken signatureEllipsis)
    : base(tok, name, hasStaticKeyword, true, typeArgs, ins, outs, req, mod, ens, decreases, body, attributes, signatureEllipsis) {
    Contract.Requires(tok != null);
    Contract.Requires(name != null);
    Contract.Requires(cce.NonNullElements(typeArgs));
    Contract.Requires(cce.NonNullElements(ins));
    Contract.Requires(cce.NonNullElements(outs));
    Contract.Requires(cce.NonNullElements(req));
    Contract.Requires(mod != null);
    Contract.Requires(cce.NonNullElements(ens));
    Contract.Requires(decreases != null);
    TypeOfK = typeOfK;
  }

  public override bool AllowsAllocation => false;
}

public class LeastLemma : ExtremeLemma {
  public override string WhatKind => "least lemma";

  public LeastLemma(IToken tok, string name,
    bool hasStaticKeyword, ExtremePredicate.KType typeOfK,
    List<TypeParameter> typeArgs,
    List<Formal> ins, [Captured] List<Formal> outs,
    List<AttributedExpression> req, [Captured] Specification<FrameExpression> mod,
    List<AttributedExpression> ens,
    Specification<Expression> decreases,
    BlockStmt body,
    Attributes attributes, IToken signatureEllipsis)
    : base(tok, name, hasStaticKeyword, typeOfK, typeArgs, ins, outs, req, mod, ens, decreases, body, attributes, signatureEllipsis) {
    Contract.Requires(tok != null);
    Contract.Requires(name != null);
    Contract.Requires(cce.NonNullElements(typeArgs));
    Contract.Requires(cce.NonNullElements(ins));
    Contract.Requires(cce.NonNullElements(outs));
    Contract.Requires(cce.NonNullElements(req));
    Contract.Requires(mod != null);
    Contract.Requires(cce.NonNullElements(ens));
    Contract.Requires(decreases != null);
  }
}

public class GreatestLemma : ExtremeLemma {
  public override string WhatKind => "greatest lemma";

  public GreatestLemma(IToken tok, string name,
    bool hasStaticKeyword, ExtremePredicate.KType typeOfK,
    List<TypeParameter> typeArgs,
    List<Formal> ins, [Captured] List<Formal> outs,
    List<AttributedExpression> req, [Captured] Specification<FrameExpression> mod,
    List<AttributedExpression> ens,
    Specification<Expression> decreases,
    BlockStmt body,
    Attributes attributes, IToken signatureEllipsis)
    : base(tok, name, hasStaticKeyword, typeOfK, typeArgs, ins, outs, req, mod, ens, decreases, body, attributes, signatureEllipsis) {
    Contract.Requires(tok != null);
    Contract.Requires(name != null);
    Contract.Requires(cce.NonNullElements(typeArgs));
    Contract.Requires(cce.NonNullElements(ins));
    Contract.Requires(cce.NonNullElements(outs));
    Contract.Requires(cce.NonNullElements(req));
    Contract.Requires(mod != null);
    Contract.Requires(cce.NonNullElements(ens));
    Contract.Requires(decreases != null);
  }
}
