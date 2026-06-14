# Performance Manifest

Rules and observations accumulated from performance work in this codebase. Each entry is a rule or research direction with evidence and rationale, so the same issues are not re-discovered later.

## Principles

### P-001: One binding per call site

`foo.Bar()` is two syntax nodes for a walker: a `MemberAccessExpressionSyntax` and an `InvocationExpressionSyntax`. If both visitors call `GetSymbolInfo` on their respective nodes, the same call site is bound twice.

Filtering the redundant case in `VisitMemberAccessExpression` eliminates the duplicate binding at the source. Caching is not a substitute: a per-node cache produces separate entries for the two nodes, so both bindings still happen, the cache just makes the second one cheap.

**Evidence:** [Acuminator#668](https://github.com/Acumatica/Acuminator/pull/668)

**When the guard applies.** When both visitors call `GetSymbolInfo`.

## Open questions

### Q-001: Other overlapping syntax-node visit patterns

P-001 addresses one specific pair. The underlying problem is general: whenever a walker performs work on both a parent syntax node and a child node that is part of the parent, the work may run twice for the same construct. This applies to specific-node visitor pairs (like the one in P-001) and to walkers that combine generic visiting with specific-node overrides. Auditing the codebase for such patterns may yield further wins.
