# AcuminatorTurbo

A personal performance playground for making [Acuminator](https://github.com/Acumatica/Acuminator) faster, leaner, and closer to ideal. Performance only. Here I try out my ideas as they come.

---

## Iteration 1: Significant performance improvement

✅ Landed in upstream as [Acuminator#668](https://github.com/Acumatica/Acuminator/pull/668) on 2026-05-08.

Introduced `SymbolInfoCache` and eliminated double-binding of symbol info for member-access invocations in `NestedInvocationWalker`. The expression `foo.Bar()` was previously processed both as `MemberAccessExpressionSyntax` and `InvocationExpressionSyntax`, so the same call site was bound twice.

| small (`PX.Objects.SV`) | medium (`PX.Objects.AM`) | large (`PX.Objects`) |
| :---: | :---: | :---: |
| ![small](perf/iterations/iteration_1/small.svg)<br>time **1.3× faster (−23%)** · memory **1.6× less (−37%)** · objects **1.3× less (−26%)** · GC **1.2× less (−14%)** | ![medium](perf/iterations/iteration_1/medium.svg)<br>time **2.8× faster (−64%)** · memory **5.8× less (−83%)** · objects **4.1× less (−76%)** · GC **3.2× less (−68%)** | ![large](perf/iterations/iteration_1/large.svg)<br>time **7.4× faster (−87%)** · memory **8.8× less (−89%)** · GC **9.6× less (−90%)** |

> ⓘ Allocated object count could not be measured for the large target. Allocation sampling never completed there, even after hours of running. Small and medium charts include it.

**Commits:** [Acuminator#668](https://github.com/Acumatica/Acuminator/pull/668) (upstream merge).

**Captured as:** [P-001: One binding per call site](perf/PERFORMANCE_MANIFEST.md#p-001-one-binding-per-call-site)

---

## For upstream maintainers

The contents of `perf/` are fork-agnostic measurement infrastructure, free to cherry-pick or adapt. Any future iteration landed here may also be submitted upstream as a focused PR on request.
