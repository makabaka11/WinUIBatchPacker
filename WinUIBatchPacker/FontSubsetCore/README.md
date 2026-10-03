# FontSubsetCore

This project vendors the PyFontTools portion of the neighboring AssFontSubset.Core
project at commit `abeaf2a431ab95572379e8501a06b09389f5173d`.

The source files in `src/` were copied from `AssFontSubset.Core/src/`. The
HarfBuzz backend and its project reference were omitted; `SubsetCore` has only
the PyFontTools branch. This keeps WinUIBatchPacker independently buildable
without a sibling checkout while preserving AssFontSubset's parsing, font
matching, subsetting, and ASS rewriting behavior.
