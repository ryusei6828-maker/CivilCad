# SXF のテスト用図面

`shapes.sfc`・`shapes.p21`・`shapes.p2z` は [shibukawa/bdf](https://github.com/shibukawa/bdf) の `converter/sxf/testdata` から取ったもの
(MIT License, Copyright (c) Yoshiki Shibukawa)。同リポジトリの `test/sxf/gen.py` が 1 つの図面を SFC と、SCADEC のライブラリの
書き方をまねた P21 (AP202) と、その P21 を ZIP にした P2Z で書いたもので、CivilCad とは別の実装で作られている。
3 つとも同じ図面なので、SFC と P21 を読んだ結果が同じになることを確かめるのに使う。
