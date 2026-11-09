# サードパーティのソフトウェアについて

## 製品(NamakeKaigi 本体)が使っているもの

| 名前 | バージョン | ライセンス | 使いみち |
| --- | --- | --- | --- |
| QRCoder | 1.8.1 | MIT | スマホから参加する機能(リモート)の QR コードを作る |
| System.Speech | 10.0.14 | MIT | 読み上げ(Windows の音声合成) |
| Microsoft .NET / ASP.NET Core / WPF | 10.0 | MIT | 実行環境。リモートの小さな Web サーバー(Kestrel)にも使う |

配布の zip(自己完結型)には、Microsoft の .NET ランタイムが含まれています(MIT ライセンス)。

## 呼び出している外部のツール(同梱していません)

次のツールは、利用者の PC に入っているものを、アプリが裏で呼び出します。このリポジトリにも配布の zip にも含まれません。各ツールの利用条件は、それぞれの提供元に従ってください。

- Claude Code(Anthropic)
- Antigravity の agy(Google)
- Codex CLI(OpenAI)
- VOICEVOX(任意。読み上げの声に使う場合のみ。利用者が別に入れる)

## テスト用(配布物には含まれません)

テストプロジェクト(`src/NamakeKaigi.Tests`)では、次のパッケージを使っています。

| パッケージ | バージョン | ライセンス |
| --- | --- | --- |
| xunit | 2.9.3 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 |
| coverlet.collector | 6.0.4 | MIT |

QRCoder・System.Speech 以外の NuGet パッケージは、製品には使っていません。
