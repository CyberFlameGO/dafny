// RUN: %dafny /compile:0 "%s" > "%t"
// RUN: %dafny /noVerify /compile:4 /spillTargetCode:2 /compileTarget:cs "%s" >> "%t"
// RUN: %dafny /noVerify /compile:4 /spillTargetCode:2 /compileTarget:js "%s" >> "%t"
// RUN: %dafny /noVerify /compile:4 /spillTargetCode:2 /compileTarget:go "%s" >> "%t"
// RUN: %dafny /noVerify /compile:4 /spillTargetCode:2 /compileTarget:java "%s" >> "%t"
// RUN: %dafny /noVerify /compile:4 /spillTargetCode:2 /compileTarget:py "%s" >> "%t"
// RUN: %diff "%s.expect" "%t"

datatype List = Nil | Cons(head: int, tail: List)

method Main() {
  var a := Nil;
  var b := Cons(5, a);
  var c := (a, b);
  var d := (c.1, c.0);
  var e := ();
  var f := (1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20);
  print a, "\n";
  print b, "\n";
  print c, "\n";
  print d, "\n";
  print e, "\n";
  print f, "\n";

  print b.head, " ", b.tail, "\n";

  var u := Up(0, 7);
  print u, "\n";
  var s := Sum(u);
  PrintSum(u, "");
  print " == ", s;
  s := SumAgain(u);
  print " (once more, that's ", s, ")\n";

  CoDt();
  AllBerry();
  TestConflictingNames();
  TestModule();

  TestGhostDestructors();
}

function method Up(m: nat, n: nat): List
  requires m <= n
  decreases n - m
{
  if m == n then Nil else Cons(m, Up(m+1, n))
}

function method Sum(xs: List): int {
  match xs  // top-level match expression
  case Nil => 0
  case Cons(x, tail) => x + Sum(tail)
}

function method SumAgain(xs: List): int {
  var r := match xs  // let expression; non-top-level match expression
    case Nil => 0
    case Cons(x, tail) => x + SumAgain(tail);
  r
}

method PrintSum(xs: List, prefix: string) {
  match xs  // match statement
  case Nil =>
  case Cons(x, tail) =>
    print prefix, x;
    PrintSum(tail, " + ");  // tail recursion
}

method CoDt() {
  var s := CoUp(0, true);
  var l := ToList(s, 4);
  print l, "\n";
  if l.Cons? {
    print l.head, " ", l.tail, "\n";
  }
  print s, "\n";
  var s' := Next(-20, s);  // not a lazy invocation
  print s', "\n";
}

codatatype Stream = Next(shead: int, stail: Stream)

function method CoUp(n: int, b: bool): Stream
{
  if b then
    CoUp(n, false)  // recursive, not co-recursive, call
  else
    Next(n, CoUp(n+1, true))  // CoUp is co-recursive call
}

function method ToList(s: Stream, n: nat): List
{
  if n == 0 then Nil else Cons(s.shead, ToList(s.stail, n-1))
}

datatype Berry = Smultron | Jordgubb | Hjortron | Hallon
predicate method IsRed(b: Berry) {
  b != Berry.Hjortron
}

codatatype CoBerry = Smultron | Jordgubb | Hjortron | Hallon  // no reason for this to be a co-datatype, but, hey, why not
predicate method IsCoRed(b: CoBerry) {
  b == CoBerry.Hjortron
}

method AllBerry() {
  var s := set b: Berry | IsRed(b);
  print s, "\n";
  var c :| IsCoRed(c);
  print c, " ", c.Hjortron?, " ", c == CoBerry.Hjortron, " ", c.Smultron?, "\n";
  var n := Inf();
  print n == Zero, "\n";
}

codatatype NatPlus = Succ(NatPlus) | Zero

function method Inf(): NatPlus {
  Succ(Inf())
}

datatype ConflictingNames = ConflictingNames1(len: int, public: char, goto: string) | ConflictingNames2(goto: string)

method TestConflictingNames() {
  var x := ConflictingNames1(42, 'q', "hello");
  print x.len, " ", x.public, " ", x.goto, "\n";
}

module Module {
  datatype OptionInt = Some(int) | None
}

method TestModule() {
  PrintMaybe(Module.Some(1701));
}

method PrintMaybe(x : Module.OptionInt) {
  match x
  case Some(n) => print n, "\n";
  case None => print "None\n";
}

datatype R = R(x: int, ghost g: int)

method TestGhostDestructors() {
  var a := R(10, 20);
  var b := a.(x := a.x + 1);
  var c := b.(x := b.x + 1, g := b.g + 1);
  var d := c.(g := c.g + 1, x := c.x + 1);
  var e := d.(g := d.g + 1);

  assert e == R(13, 23);
  expect e.x == 13;
}
