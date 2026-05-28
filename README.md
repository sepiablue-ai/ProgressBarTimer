# ProgressBarTimer

Windows 向けのシンプルなカウントダウンタイマーです。常に手前に表示される小さなウィンドウで、残り時間をバーと数字で確認できます。

## 特徴

- デフォルトは 10 分
- カスタムタイトルバー付きのコンパクトなダーク UI
- 画像生成したボタンアセットを使った操作ボタン
- 残り 30% で警告色に変化
- 時間切れ後はオーバータイムとして赤いバーが左から伸びる
- ウィンドウ位置、サイズ、設定時間を `timer_config.ini` に自動保存

## 操作

| 操作 | 内容 |
|------|------|
| `Space` / `Enter` | スタート / 一時停止 |
| `R` | リセット |
| `Up` / `Down` | 設定時間を 1 分ずつ増減 |
| `-` / `+` ボタン | 設定時間を 1 分ずつ増減。長押しで連続変更 |
| 再生 / 一時停止ボタン | スタート / 一時停止 |
| 数字キー 1-2 桁 | 分数を直接入力。例: `1`, `5` で 15 分 |
| 上部バーをドラッグ | ウィンドウ移動 |
| ウィンドウ端をドラッグ | サイズ変更 |

## ファイル構成

```text
timer.cs                         C# ソースコード
timer.csproj                     .NET プロジェクトファイル
build.bat                        Windows 用ビルドスクリプト
timer.exe                        ビルド済み実行ファイル
timer_config.ini                 設定保存ファイル
assets/buttons/btn_minus.png     ボタン画像
assets/buttons/btn_play.png      ボタン画像
assets/buttons/btn_pause.png     ボタン画像
assets/buttons/btn_plus.png      ボタン画像
```

## ビルド

Windows で `build.bat` を実行します。

```bat
build.bat
```

`.NET SDK 6+` がある場合は、自己完結型の単一 EXE として `timer.exe` を生成します。`.NET SDK` が無い場合は、Windows 標準の .NET Framework コンパイラでビルドを試みます。

## 配布について

`build.bat` で生成した `timer.exe` は、基本的に EXE 単体で他の Windows PC に渡して動かせます。

- `.NET SDK 6+` でビルドされた場合: 自己完結型なので .NET の追加インストールなしで動作します。
- `.NET Framework` の `csc.exe` でビルドされた場合: Windows 10/11 に標準搭載されている .NET Framework 上で動作します。
- ボタン画像は EXE に埋め込まれるため、`assets` フォルダを同梱しなくても表示されます。
- 初回起動後、同じフォルダに `timer_config.ini` が作成または更新されます。

配布時は `timer.exe` だけで十分ですが、設定を固定したい場合は `timer_config.ini` も一緒に渡してください。
