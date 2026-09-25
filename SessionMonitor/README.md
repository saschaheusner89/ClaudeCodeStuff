# Claude Session Monitor

Ein kleines Windows-11-Fenster, das live den Status aller laufenden Claude-Code-Sessions zeigt,
und in jeder Session die gerade laufenden **Agents** als kleine Extra-Fenster.

| Farbe | Bedeutung |
|---|---|
| Blau, sanft pulsierend | **working**: Claude arbeitet |
| Gelb/Orange, deutlicher pulsierend | **wartet auf dich**: nur bei Rückfragen, Berechtigungen, Plan-Freigabe |
| Grün, ruhig | **idle**: fertig, bereit für den nächsten Prompt |
| Grau | arbeitet laut Status, aber seit 15 min kein Lebenszeichen (abgestürzt oder langer Befehl) |

Das Fenster teilt sich nach seiner Form auf: Ziehst du es als breite Leiste, stehen die Sessions
nebeneinander. Als hohe Leiste stehen sie untereinander, sonst im Raster. Dasselbe gilt für die
Agent-Fensterchen innerhalb einer Session.

## Installation

1. `ClaudeStatus-win-x64.zip` herunterladen. Das geht über das Release **session-monitor-latest**
   oder über den letzten Lauf unter *Actions → SessionMonitor* (Artefakt).
2. In einen festen Ordner entpacken, z. B. `C:\Tools\ClaudeStatus\`. Beide `.exe` müssen im selben Ordner bleiben.
3. `ClaudeStatus.exe` starten und **„Hooks in Claude Code installieren“** klicken.
   Alternativ geht `ClaudeStatus.exe --install` bzw. der Rechtsklick auf das Fenster.
   Das trägt `claude-status-hook.exe` in `%USERPROFILE%\.claude\settings.json` ein.
   Vorher wird eine Sicherung als `settings.json.bak-…` angelegt, eigene Hooks bleiben erhalten.
4. Claude Code bzw. die Desktop-App neu starten. Neue Sessions erscheinen automatisch.

Wird der Ordner verschoben, einfach erneut „Hooks installieren“ klicken.
Zum Entfernen: Rechtsklick → „Hooks entfernen“ oder `ClaudeStatus.exe --uninstall`.

> Windows SmartScreen kann beim ersten Start warnen, weil die exe nicht signiert ist:
> „Weitere Informationen“ → „Trotzdem ausführen“.

## Bedienung

- **Klick auf eine Session** holt die Claude-Desktop-App in den Vordergrund.
  Eine bestimmte Session direkt öffnen kann die Desktop-App von außen (noch) nicht.
- **Rechtsklick auf eine Session**: im Terminal fortsetzen (`claude --resume <id>`), Projektordner öffnen,
  Session-ID kopieren, aus der Anzeige entfernen.
- **Rechtsklick auf den Hintergrund**: Immer im Vordergrund, Hooks installieren/entfernen, Datenordner.
- Taste **T**: Immer im Vordergrund an/aus.

## Wie es funktioniert

```
Claude Code ──(Hook-Events)──► claude-status-hook.exe ──► %LOCALAPPDATA%\ClaudeStatus\sessions\<id>.jsonl
                                                                         │
                                          ClaudeStatus.exe ◄──(liest alle 250 ms)──┘
```

- `claude-status-hook.exe` ist Native AOT und startet in wenigen Millisekunden. Es gibt nie etwas aus
  und endet immer mit Exit-Code 0, damit es Claude Code weder bremst noch stört.
- Ausgewertete Events: `SessionStart/End`, `UserPromptSubmit`, `Pre/PostToolUse`, `PermissionRequest`,
  `Notification` (`permission_prompt`, `elicitation_dialog` = Rückfrage, `idle_prompt` wird ignoriert),
  `Stop`, `SubagentStart/Stop` sowie `AskUserQuestion` und `ExitPlanMode` als Rückfrage.
- Agents werden über den `Agent`/`Task`-Tool-Aufruf (Beschreibung, Typ) und `SubagentStart/Stop`
  (`agent_id`) verfolgt. Fertige Agents bleiben 45 s sichtbar.
- Sessions ohne `SessionEnd` verschwinden nach 8 h Leerlauf. Alte Logdateien werden nach 2 Tagen gelöscht.

## Entwicklung

```
dotnet test tests/ClaudeStatus.Tests      # Logik (läuft auch unter Linux)
dotnet run --project src/ClaudeStatus     # nur unter Windows
```

`ClaudeStatus.Core` enthält die gesamte Logik (Statusmodell, Layout, Installer) ohne WPF und ist getestet.
