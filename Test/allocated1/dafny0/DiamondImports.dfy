// RUN: %dafny_0 /verifyAllModules /allocated:1 /print:"%t.print" /dprint:"%t.dprint" "%s" > "%t"
// RUN: %diff "%s.expect" "%t"
include "../../dafny0/DiamondImports.dfy"
