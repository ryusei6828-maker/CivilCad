# JWW のテスト用図面

`shapes.jww` と `old.jww` は [shibukawa/bdf](https://github.com/shibukawa/bdf) の `converter/jww/testdata` から取ったもの
(MIT License, Copyright (c) Yoshiki Shibukawa)。同リポジトリの `test/jww/gen.py` が、Jw_cad の公開されている
データ形式 (jwdatafmt.txt) どおりに書いた図面で、CivilCad の書き出しとは別の実装で作られている。

- `shapes.jww`: データ版 700 (Jw_cad 7)、A3、レイヤグループの縮尺 1/50。線色・線種・線幅、円・円弧・楕円、点、
  文字 (縦書き含む)、寸法、ソリッド (四角形・任意色・円のソリッド)、入れ子のブロック、非表示のグループとレイヤ
- `old.jww`: データ版 300 (Jw_cad 3)、A4。図形ごとの線幅・SXF の表が無く、ブロック定義の時刻が 4 バイト
