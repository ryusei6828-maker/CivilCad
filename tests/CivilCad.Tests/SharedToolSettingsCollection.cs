namespace CivilCad.Tests;

/// <summary>
/// アプリ全体で共有する設定 (計測の記入のしかた・作図の入力の記憶・拡大縮小 / 回転 / 範囲選択のやり方など) を
/// 書き換えるテストのまとまり。同じまとまりのテストは同時に走らないので、設定の取り合いで落ちない。
/// </summary>
[CollectionDefinition("SharedToolSettings")]
public class SharedToolSettingsCollection;
