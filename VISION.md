# Gitster – Vision

## Was ist Gitster?

Gitster ist kein weiterer vollständiger Git-Client. Es ist ein **chirurgisches Werkzeug** für Entwickler, die präzise Kontrolle über Git-Metadaten, History und unkonventionelle Workflows benötigen – Dinge, die in GitHub Desktop, Fork oder GitKraken entweder gar nicht oder viel zu umständlich möglich sind, und für die GitButler den Workflow zu sehr neu erfindet.

**Positionierung:** Gitster ergänzt einen normalen Git-Workflow. Es ist das Tool, das man aufmacht, wenn man eine präzise History-Operation braucht – nicht das, in dem man den ganzen Tag arbeitet.

---

## Zielgruppe

Entwickler, die Git gut kennen, aber bestimmte Metadaten- und History-Operationen regelmäßig brauchen und keine Lust haben, dafür jedes Mal komplexe `git rebase -i`- oder `git log -S`-Befehle zu schreiben.

---

## Lizenz & Geschäftsmodell

**Open Source, kostenlos, mit Patreon-Modell.**

- Code ist frei und open-source, auf GitHub.
- Stabile Releases sind kostenfrei für alle.
- Patreon-Unterstützer bekommen:
  - **Preview-Versionen** mit neuen Features vor dem Mainline-Release
  - **Stärkeres Mitspracherecht** bei der Priorisierung neuer Features (Polls, direkte Diskussion)
  - Sichtbarkeit als Sponsor (optional)
- Kein Tracking, keine Telemetrie, keine Cloud-Anbindung, keine plötzlichen Paywalls.

Das positioniert Gitster bewusst gegen die Marktentwicklung der letzten Jahre: GitKraken ist zu Subscription gewechselt, SmartGit hat Features aus der Free-Version entfernt, Tower kostet ~$80/Jahr. Vertrauen darin, dass Gitster bleibt was es ist, soll ein eigenes Verkaufsargument sein.

---

## Was Gitster nicht werden soll

- Kein vollständiger Merge-Konflikt-Editor
- Kein Ersatz für `git log --graph` mit voll ausgebauten Multi-Branch-Linien (vereinfachte Single-Branch-Linie ist okay)
- Keine Cloud-Integration, keine Issue-Tracker-Anbindung, kein PR-Workflow
- Keine KI-Features (keine generierten Commit-Messages, keine Branch-Namen-Vorschläge, kein Agent-Modus)
- Keine Repository-Verwaltung (Clone, Init, Repository-Settings jenseits von Config-Healthcheck)
- Kein Datei-Browser, keine Commit-Authoring-Hilfe für *neue* Commits, **außer** im Empty-Repo-Kontext (siehe Backlog)
- Keine Telemetrie, keine Tracking-Pixel, keine Nutzungsanalyse

---

## UI-Architektur

Die übergeordnete Layout-Entscheidung, die alle Features tragen muss:

### Modus-Sidebar links

Schmale (44px) Icon-Sidebar links, die zwischen Hauptbereich-Modi umschaltet. Inspiriert von VS Code, Linear, JetBrains. Sechs Modi:

- **Commits** – Standard-Ansicht, die heutige Hauptansicht
- **Stashes** – Stash-Killer (Phase 2)
- **Branches** – Branch-Verwaltung, sortiert nach letzter Aktivität
- **Worktrees** – Worktree-Verwaltung (Phase 3)
- **Search** – Pickaxe, Diff-Regex, Blame (Phase 4)
- **Operations log** – Operations-History-Browser

Eine Modus-Auswahl tauscht den Hauptbereich aus. Die kontextuelle Action-Spalte rechts und die Status-Bar unten bleiben — beide bekommen modus-spezifische Inhalte. TitleBar und Menü-Leiste bleiben global.

Badges an den Sidebar-Icons signalisieren Aufmerksamkeitsbedarf (z. B. „3" am Stash-Icon wenn drei Stashes vorhanden).

### TitleBar mit Akzent-Hintergrund

Volle Akzent-Sättigung (`AccentBlue`), weiße Schrift. Das ist die Identitäts-Zone — Repo-Name, Branch, Ahead/Behind-Counter, Auto-Fetch-Toggle, Switch-Repo-Button.

### Switch repo als SplitButton mit Pinned + Recent

Der Switch-Repo-Button in der TitleBar ist ein SplitButton: Hauptklick öffnet den File-Browser-Dialog, der Chevron daneben öffnet ein Dropdown mit zwei Sektionen:

- **Pinned** — manuell angeheftete Lieblings-Repos, Sortierung manuell
- **Recent** — bis zu 10 zuletzt geöffnete Repos, neueste oben

Jeder Recent-Eintrag hat ein kleines Pin-Icon zum Promovieren in Pinned. Pinned-Einträge zeigen das gefüllte Pin-Icon und können per Rechtsklick entfernt werden. Persistenz in `%AppData%/Gitster/recent-repos.json`.

### Klickbare Status-Bar-Texte

Die Status-Bar-Texte sind nicht nur Anzeigen, sondern Eingangstüren zu zugehörigen Aktionen:

- **„0 modified, 1 staged, 0 untracked"** → öffnet das Commit-Panel
- **Branch-Name** → öffnet den Branch-Switch-Dialog (später, mit Phase 3)
- **Repo-Pfad ganz rechts** → öffnet den Pfad im Explorer

Das löst das Problem „selten gebrauchte Aktionen vergisst man, wo sie sind" — die Aktion findet sich da, wo der zugehörige Status angezeigt wird.

---

## Aktueller Stand

Stand: Phase 1 ist vollständig abgeschlossen ✅. Phase 2a (Modus-Sidebar) ist vollständig implementiert ✅. Phase 2b (Stash-Killer) ist als nächstes geplant. Die folgenden Sektionen markieren erledigte Features mit ✅, in Arbeit mit 🔧, und noch offen mit ⬜.

---

## Phase 0 – Bereits umgesetzt ✅

- ✅ **Commit-Zeitstempel ändern** – Datum und Uhrzeit des letzten Commits nachträglich anpassen (Author + Committer)
- ✅ **Zeit aus Commit übernehmen** – Zeitstempel eines beliebigen Commits in den Datepicker laden
- ✅ **Remote-Operationen** – Fetch, Pull, Push, Sync mit Remote-Auswahl direkt in der UI
- ✅ **Commit-Liste mit Filter** – Commits nach Autor und weiteren Kriterien filtern
- ✅ **Eigener DateControl** – Komfortabler Datums-/Zeitpicker mit Tastaturkürzeln und Kalender-Popup

---

## Phase 1 – Fundament: Sicherheitsnetz & Metadaten-Chirurgie

Bevor Gitster destruktive Operationen über mehrere Commits hinweg anbietet, braucht es ein robustes Undo. Diese Phase legt das Fundament für alles Weitere.

### Bereits erledigt

- ✅ **Operations-Log** – Jede destruktive Aktion wird als rückgängig machbarer Eintrag protokolliert. Persistent in `.git/gitster/operations.json`.
- ✅ **Undo** – Letzten Vorgang rückgängig machen, auch nach Amend. Confirmation-Dialog bei intermediate commits.
- ✅ **Zustandsanzeige vor jeder Operation** – Klarer Hinweis, was sich ändert (betroffene Commits, SHA-Vorschau).
- ✅ **Sicherer Force-Push** – Alle Push-Operationen nach Amend/Rebase verwenden `--force-with-lease`.
- ✅ **Status-Bar mit Live-Watch** – Working-Tree-State (modified/staged/untracked) und laufende Operationen werden live angezeigt, getriggert über FileSystemWatcher.
- ✅ **Auto-Fetch mit Toggle** – Optionales periodisches Fetch alle 60s, in der TitleBar an-/abschaltbar, pausiert bei minimiertem Fenster.
- ✅ **`IGitBackend`-Abstraktion** – Backend-Schicht über LibGit2Sharp, vorbereitet für künftige Git-CLI-Implementierung in Phase 2+.
- ✅ **Capability-System** – Attached Property `Capability.Requires="..."` für Buttons/Menü-Items. Zeigt strukturell-disabled-Indikator mit erklärendem Tooltip.
- ✅ **Operations-Log-Viewer** – Modaler Dialog, gefiltert nach Status und Kind.
- ✅ **Menü-Leiste** – File, Repository, Edit, View, Help mit Tastaturkürzeln.
- ✅ **Recent Repos** – Bis zu 10 zuletzt geöffnete Repos, persistent in `%AppData%/Gitster/`.
- ✅ **Repository Settings Dialog** – Hülle mit General + Git config healthcheck Tab.
- ✅ **Window-Title** – `<RepoName> · <Branch> – Gitster` für eindeutige Taskbar-Identifikation bei mehreren Instanzen.

### Phase-1-Abschluss (geplant)

- ✅ **Critical bug fixes** – Empty-Repo-Handling (NRE), Repo-Switch-State-Leak, Undo-Refresh, Replaced-Status auf konsekutive Amends, stabile SHA-basierte Undo-Targets statt `HEAD@{n}`.
- ✅ **Drei-Sektionen-Commit-Liste mit Sicherheits-Indikatoren** – Section-Header in Reihenfolge **Incoming → Outgoing → Synced**, jeweils farbig getönt (blau / amber / gray). Leere Sektionen werden ausgeblendet. Per Commit ein Status-Dot als zusätzliche Redundanz: blau gefüllt (Incoming), amber gefüllt (Outgoing), Ring-Outline (Synced). Sicherheits-Indikatoren *dürfen* sich wiederholen — das Status wird in der Liste, auf der Selected-Commit-Card, im Diff-Header und im TimestampEditPanel angezeigt.
- ✅ **Sicherheitswarnung beim Amenden von Synced-Commits** – Banner im TimestampEditPanel: „This commit is on origin/master — amending will require force-push and affect others." Amend bleibt möglich, wird aber sichtbar als unsicher markiert.
- ✅ **Kombiniertes Amend für alle Metadaten** – Ein einzelner „Amend"-Button wendet alle Änderungen zusammen an: neuer Timestamp, Author, Committer. Die Vorschau-Box zeigt Timestamp-Zeile + Author-Zeile mit „→ new" wenn Author geändert. Kein separates „Apply" für Author mehr.
- ✅ **Author/Committer-Bearbeitung über Dialog** – Kompaktes Schnell-Dropdown im Action-Panel; „Edit..."-Button öffnet modalen `EditAuthorsDialog` mit separater Author/Committer-Auswahl und Sync-Checkbox.
- ✅ **Autor reparieren über Range** – Falschen Namen/E-Mail über mehrere Commits hinweg korrigieren, mit Live-Preview welche Commits betroffen sind, Warnung bei Synced-Commits.
- ✅ **Zeitstempel über Range umschreiben** – Drei Modi: Shift-Offset (z. B. „alle 2h früher"), Distribute (gleichmäßig zwischen zwei Zeiten), Working-Hours (Mon–Fri 9–18). Phase 1 liefert mindestens Shift-Offset; die anderen Modi sind Stretch-Goal.
- ✅ **Repository-Snapshots** – Inspiriert von GitUp: zusätzlich zum Operations-Log nimmt Gitster automatisch Snapshots des kompletten Ref-States auf, wenn sich HEAD bewegt – egal ob durch Gitster, durch Terminal-Befehle, durch eine andere IDE. Speicherung in `.git/gitster/snapshots/`. Retention: 7 Tage voll, dann 90 Tage täglich, dann gepruned. Browser-UI zum Wiederherstellen ist Backlog – Phase 1 implementiert nur die Capture-Mechanik.
- ✅ **TitleBar mit Akzent-Hintergrund** – `AccentBlue` als TitleBar-Hintergrund, weiße Schrift.
- ⬜ **SplitButton für Switch repo mit Pinned/Recent** – Hauptbutton + Chevron-Dropdown mit zwei Sektionen (Pinned, Recent). Pin-Icon pro Recent-Eintrag zum Promovieren. Persistenz erweitert das bestehende `RecentReposService`.
- ⬜ **Klickbarer Working-Tree-Text in Status-Bar** – Klick auf „N modified, M staged, K untracked" öffnet das Commit-Panel.
- ✅ **Copy-Buttons auf Commit-Hash und Message** – In der Selected-Commit-Card. Hash kopiert die volle SHA (40 Zeichen), Message kopiert den Volltext. Kurzes „Copied!"-Feedback im Tooltip.
- ✅ **„Selected commit"-Label entfernen** – Die Card ist durch ihre Position und Inhalt selbsterklärend. Label entfernt.
- ✅ **DateControl mit Akzent-Border zurück** – Die ursprüngliche Akzent-Border war im Tool-Kontext richtig; aktuell zu blass.
- ✅ **Topologische Commit-Sortierung als Default** – Statt nach Date (das bei manipulierten Timestamps Parent vor Child stellen kann). Date-Sort bleibt optional mit Hinweis.
- ✅ **Simple Single-Branch-Graph** – Vertikale Linie + Punkte in einer schmalen Spalte links der Commit-Liste. Volle Multi-Branch-Visualisierung ist Backlog.
- ✅ **Visualisierung verwaister Hash-Paare (Detection + ↔ Indikator)** – Wenn ein Synced-Commit lokal amendet wird, taucht der alte Hash als Incoming und der neue als Outgoing auf. Gitster erkennt das Paar anhand desselben Tree-SHA und zeigt ein ↔-Badge mit Tooltip: „Same commit, rewritten. Push (force-with-lease) to replace the remote copy." Der visuelle Verbindungsstrich zwischen den Zeilen ist als TODO markiert.
- ✅ **Git-Config Gesundheitscheck-Inhalt** – Sieben Empfehlungen: `diff.algorithm=histogram`, `fetch.prune=true`, `rerere.enabled=true`, `push.autoSetupRemote=true`, `branch.sort=-committerdate`, `init.defaultBranch=main`, `pull.rebase=true` (markiert als „opinionated").
- ✅ **Detached HEAD klar anzeigen** – Im TitleBar und StatusBar: „detached @ {sha}" statt „(no branch)", um Detached-HEAD-Zustand eindeutig kenntlich zu machen.
- ✅ **Diff für initiale Commits korrekt** – Diff des ersten Commits (kein Parent) vergleicht gegen den leeren Tree; Status-Badge (A/M/D/R) pro Datei in der Diff-Liste.
- ✅ **Window-Placement-Persistenz** – Fensterposition, Größe und Maximierungszustand werden in `%AppData%/Gitster/window-settings.json` gespeichert. Restore nutzt `RestoreBounds`, um den klassischen Maximiert-Bug zu vermeiden. Off-Screen-Positionen fallen auf CenterScreen zurück.

---

## Phase 2a – Modus-Sidebar als Vorphase ✅

Bevor Stash-Killer und nachfolgende Features kommen können, braucht Gitster die strukturelle Erweiterung um die Modus-Sidebar. Das ist Architektur-Arbeit, keine Feature-Arbeit, deshalb als eigene Vorphase markiert.

- ✅ **`AppMode`-Enum** – Commits, Stashes, Branches, Worktrees, Search, OperationsLog in `Models/AppMode.cs`.
- ✅ **`SidebarViewModel`** – `CurrentMode`, `IsXxxActive`-Properties, `StashCount`, `ActiveOpsCount`, `SelectModeCommand`.
- ✅ **Sidebar-Control** – 44px breite `Sidebar.xaml` UserControl mit Icon-Buttons, aktivem-Modus-Highlight (blauer 3px Balken + blaues Icon), Badge-Dots für Stash-Count und aktive Ops-Log-Einträge. Stroke-Icons für alle sechs Modi.
- ✅ **Commits-Modus** – Bestehender 3-Spalten-Inhalt in `Views/Modes/CommitsModeView.xaml` verschoben; DataContext von MainWindowViewModel geerbt.
- ✅ **Operations-Log-Modus** – Inhalt des Operations-Log-Dialogs als `OperationsLogModeView.xaml` UserControl portiert; modaler Dialog bleibt erhalten.
- ✅ **Modus-Routing in `MainWindowViewModel`** – `SidebarVM`-Property, `OpsLogService`-Property, `RefreshSidebarBadgesAsync()` in `UpdateElementsAsync()`, Ops-Log-Badge via `Changed`-Event.
- ✅ **`GetStashCountAsync()`** – In `IGitBackend` + `LibGit2Backend` implementiert.
- ✅ **Stubs für vier weitere Modi** – `StashesModeView`, `BranchesModeView`, `WorktreesModeView`, `SearchModeView` mit Platzhalter-Inhalt in `Views/Modes/`.
- ✅ **Ctrl+1–6 Tastaturkürzel** – Über `Window.InputBindings` direkt an `SidebarVM.SelectModeCommand` gebunden.
- ✅ **View-Menü** – Sechs Modus-Einträge mit Tastaturkürzel-Anzeige.

Gitster ist nach Phase 2a funktional identisch zu Phase 1, aber strukturell bereit für alle Folge-Features.

---

## Phase 2b – Stash-Killer & Fixup-Workflow

Diese Phase liefert das vermutlich universellste Killer-Feature von Gitster (Stash-Verwaltung) und macht das Bearbeiten *bestehender* Commits zur Standardoperation.

### Warum Stash-Killer hier vorgezogen wird

Die Recherche zu universellen Schmerzpunkten in Git-GUIs hat ergeben: anonyme Stash-Namen, fehlende Stash-Suche und fehlende Vorschau treffen *jeden* Git-Nutzer, nicht nur Power-User. Selbst kommerzielle Premium-Tools (GitKraken, Tower, Fork) lösen das nicht zufriedenstellend. Das ist die niedrigste Eintrittshürde, mit der Gitster sofortigen Mehrwert bietet – ideal für ein Release, das Reichweite aufbauen soll.

### Features

- **Stash-Killer** – Alle Stashes als benannte, durchsuchbare Liste:
  - Auto-Naming aus dem Stash-Inhalt: `stash@{2}` → „wip: 3 files in src/auth · login.tsx, auth.ts, +1"
  - Anzeige des Branch-Kontexts zum Stash-Zeitpunkt (Information die Git intern speichert, aber kein GUI zeigt)
  - Filter über Stash-Name, betroffene Dateien, Branch
  - Inline-Diff-Vorschau pro Stash
  - **„Convert to branch"** als prominente Aktion – ein Klick: Stash wird zu einem benannten Branch, Stash wird entfernt. Das ist Gitsters Haltung: „Wenn du ihn länger behältst als ein paar Stunden, gehört er nicht in den Stash-Stack."
  - Standard-Aktionen (Apply, Pop, Drop, Rename) als sekundäre Buttons
- **Fixup per Klick** – Staged Changes einem beliebigen Commit aus der Liste zuweisen (kein SHA-Tippen); Gitster führt intern `--fixup` + `--autosquash` aus
- **Reword beliebiger Commit** – Commit-Message irgendwo in der History direkt im UI ändern, nicht nur HEAD, via `git commit --fixup:reword=[sha]`
- **Autosquash-Vorschau** – Vor dem Ausführen anzeigen, welche Commits in welcher Reihenfolge zusammengeführt werden
- **Squash mit Datumskontrolle** – Commits zusammenführen und gezielt wählen, welches Datum und welche Message behalten werden
- **Cherry-pick mit Zeitstempel** – Commit aus einem anderen Branch übernehmen, dabei Datum überschreiben

---

## Phase 3 – Branch-Operationen & Custom Tools

Diese Phase eliminiert Kontextwechsel zwischen Branches und macht Gitster für jeden User individuell anpassbar.

- **Commit auf anderen Branch** – Staged/unstaged Dateien direkt in einen anderen Branch committen, ohne `stash` oder `switch` – der aktuelle Workingstate bleibt unangetastet
- **Branch-Snapshot** – Aktuellen Stand als neuen Branch sichern (leichtgewichtiger als Stash, benannter als Stash)
- **Worktrees als First-Class-Citizen** – Worktrees anlegen, wechseln, im Dateisystem öffnen, verwaiste aufräumen. Die richtige Antwort auf „ich muss schnell auf einem anderen Branch arbeiten", aber heute praktisch unbenutzbar wegen CLI-Friktion.
- **Branch-Liste nach Datum** – Branches sortiert nach letztem Commit-Datum statt alphabetisch (niemand will `feature/xyz-old` ganz oben sehen)
- **Custom Tools Menu** – Ein Feature, das `git gui` seit 15 Jahren hat und das *kein* modernes GUI übernommen hat: User definieren in `.gitconfig` oder in den Gitster-Settings Custom-Commands (`[guitool "name"]`-Sektionen oder ein eigenes Gitster-Format), die als Menüeinträge erscheinen. Beispiel: ein User-defined-Command „Create feature branch" prompted nach einem Namen und führt `git checkout development && git checkout -b feature__$1 development` aus. Ideal für Teams mit eigenem Branching-Modell. Repository-spezifische Commands überschreiben globale.

---

## Phase 4 – Suche & Analyse

Die mächtigsten Git-Features sind alle CLI-only und kaum bekannt. Diese Phase macht sie zugänglich – das sind die Features, die kein anderes GUI hat (mit Ausnahme von GitUp, das Mac-only ist).

- **Pickaxe-Suche** – `git log -S "string"` als GUI: Finde jeden Commit in der gesamten History, bei dem ein bestimmter String hinzugefügt oder entfernt wurde. Ideal für Security-Audits, Debugging, „wann wurde diese Funktion gelöscht?"
- **Diff-Suche mit Regex** – `git log -G "regex"` als GUI: Alle Commits anzeigen, die einen Pattern im Diff enthalten
- **Blame mit Code-Verfolgung** – `git blame -w -C -C -C` als Ansicht: Blame, der Whitespace-Änderungen ignoriert und Code durch Umbenennungen/Refactorings verfolgt. Kein einziges GUI auf Windows implementiert das ordentlich.
- **Range-diff-Visualizer** – `git range-diff` als GUI: Nach einem Rebase sehen, was sich an den eigenen Commits *außer* dem Parent geändert hat. Unverzichtbar vor Force-Push und für Patch-basiertes Review.
- **Diff-Comparison zwischen beliebigen Refs** – Klar gekennzeichneter Drei-Punkte- vs. Zwei-Punkte-Diff (`A...B` vs. `A..B`), den heute niemand korrekt erklärt bekommt
- **Diff-Ansicht des ausgewählten Commits** – Kompakte Anzeige der Änderungen eines ausgewählten Commits direkt in Gitster (Grundlage für die Suche-Ergebnisse)

---

## Phase 5 – Commit-Chirurgie

Die komplexesten Operationen kommen zuletzt, weil sie das Sicherheitsnetz aus Phase 1 und die Diff-Infrastruktur aus Phase 4 brauchen. Hier orientiert sich Gitster bewusst an GitButler – aber als gezielte Operation, nicht als ständiger Workflow.

- **Commits umsortieren** – Drag-and-drop-Reihenfolge von Commits im aktuellen Branch ändern, ohne `rebase -i` anzufassen
- **Commits aufteilen** – Einen Commit in mehrere kleinere aufbrechen, mit Auswahl, welche Dateien/Hunks in welchen neuen Commit wandern
- **Commits zusammenführen** – Mehrere Commits per Auswahl squashen, mit Kontrolle über Datum und Message des Ergebnis-Commits
- **Orphan-Branch erstellen** – Neuen Branch ohne History starten, direkt aus der UI
- **Detached HEAD aufräumen** – Übersichtliche Verwaltung von detached-HEAD-Zuständen mit „in Branch retten"-Aktion

---

## Backlog

Ideen, die zum Profil passen, aber nicht in den ersten Phasen priorisiert sind. Reihenfolge ist hier bewusst nicht festgelegt – das wird nach Nutzer-Feedback aus den ersten Phasen entschieden, und Patreon-Unterstützer haben dabei stärkeres Mitspracherecht.

### Empty-Repo-Onboarding (Konzept noch zu schärfen)

**Status:** Phase 1 macht graceful failure bei leeren Repos – Liste leer, keine Aktionen möglich, kein Crash. Das ist genug für jetzt. Aber konzeptionell offen: wenn ein User `git init` macht und Gitster aufmacht, wäre eine geführte Hilfestellung sinnvoll. Das durchbricht aber das „nur bestehende History"-Profil und braucht Designarbeit.

Vorbild-Vergleich noch zu machen: wie lösen Fork, GitKraken, GitHub Desktop das? Mögliche Richtungen:

- **Onboarding-Card im Leeren-Liste-Zustand:** „This repository has no commits yet. Set up your identity and make the first commit." mit drei Schritten: `user.name`/`user.email` prüfen → `.gitignore` Template-Auswahl aus [github/gitignore](https://github.com/github/gitignore) → erster Commit aller getrackten Dateien.
- **Erweiterung Phase 1 nachträglich:** wenn das Empty-Repo-Konzept klar ist, könnte es nachgereicht werden, ohne andere Phasen zu blockieren.
- **Outsourcing an System-Git-GUI:** im Empty-Zustand einfach „Open in <Git GUI>"-Button anbieten, der `git gui` oder den System-Standard öffnet. Pragmatisch, profil-treu, aber halbgar.

Diese Entscheidung kommt später. Phase 1 ist hier explizit minimal.

### Komfort & Workflow
- **Zeitstempel-Vorlagen** – Gespeicherte Zeit-Presets („gestern 09:00", „letzten Freitag 17:30") für schnelles Befüllen
- **Mehrfach-Amend-Queue** – Mehrere Commits in einer Session mit verschiedenen Zeiten amenden, als Batch ausführen
- **Snapshot-Browser-UI** – Volle UI für die Phase-1-Snapshots: Zeitstrahl, Vergleich zwischen Snapshots, Wiederherstellen einzelner Branches statt des kompletten Repo-Zustands
- **.gitignore-Template-Auswahl** – Integration von [github/gitignore](https://github.com/github/gitignore) für sinnvolle Vorlagen pro Sprache/Framework. Sinnvoll auch außerhalb des Empty-Repo-Falls — z. B. „add Python-Template to existing .gitignore".

### Visualisierung
- **Multi-Branch-Graph** – Vollständige Branch-Linien-Darstellung mit Parallel-Branches, Merges, Rebases. Phase 1 hat nur die Single-Branch-Spine.
- **Gravatar-Avatare** – Kleine Avatare neben Autor-Namen in der Commit-Liste. Über E-Mail-Hash an Gravatar (opt-in, da Privacy-relevant).

### Erweiterte Repo-Hygiene
- **Submodul-Status auf einen Blick** – Kompakte Liste „diese Submodule sind dirty, diese hinken hinterher" – heute nirgends sichtbar
- **Rerere-Visualizer** – Liste der gespeicherten Konflikt-Auflösungen, inspizierbar und löschbar
- **Hook-Editor** – `.git/hooks/`-Skripte editieren, testen, bekannte Snippets anbieten („verhindere Commits in main", „verhindere große Dateien")
- **Reflog-Browser** – Den Reflog als brauchbare, durchsuchbare UI – „zeig mir, wo HEAD vor 3 Stunden war"

### Sicherheit & Signing
- **Signing-Management & Verifikation** – Sichtbar machen, welche Commits signiert sind und mit welchem Schlüssel; Signaturen beim Amend nachträglich hinzufügen (GPG/SSH/gitsign). Verifikations-Badges in der Commit-Liste.

### Power-User-Nischen
- **Bundle-URI / Partial-Clone-Verwaltung** – Neue Git-Features (`--filter=blob:none`, bundle-uri), die noch kein GUI exponiert

---

## Performance als Stillschweigendes Versprechen

Gitster zielt nicht auf Linux-Kernel-Sized-Repos (>900k Commits) ab – das ist GitUp-/Lazygit-Territorium. Aber Gitster muss bei **typischen Repos bis 50.000 Commits flüssig** bleiben:

- Commit-Liste mit `VirtualizingStackPanel`, nicht voll geladen
- Diff-Berechnung asynchron, mit Cancellation bei schneller Auswahl
- Snapshots im Hintergrund, nie blockierend

Das ist kein Marketing-Feature, sondern ein nicht-verhandelbares Qualitätsmerkmal. Wenn Gitster bei 10.000 Commits hängt, hat es seine Zielgruppe verfehlt.

---

## Marketing-Narrativ

Drei Säulen, in dieser Reihenfolge:

1. **„Undo für Git, das wirklich funktioniert."** – Operations-Log + Snapshots + sichere Force-Pushes. Das ist die emotionale Hook, die Vertrauen schafft.
2. **„Stashes endlich verwaltbar."** – Phase 2 macht ein Feature universell zugänglich, das heute jeden Git-User schmerzt.
3. **„Die Profi-Features aus der CLI, ohne CLI."** – Pickaxe, Range-diff, intelligenter Blame. Für die, die wissen, was sie wollen.

**Bewusst nicht im Marketing:**

- Die Zeitstempel-Manipulation wird *nicht* als Hero-Feature inszeniert. Sie ist ein Werkzeug unter vielen, weil sie sonst die falsche Zielgruppe anzieht (Contribution-Faker statt Profis).
- Kein direkter Wettbewerb mit GitButler. GitButler erfindet den Workflow mit Virtual Branches neu. Gitster ergänzt den klassischen Git-Workflow um Operationen, die in der CLI weh tun.

---

## Risiken & Disziplin

- **Feature-Creep ist die größte Gefahr.** Jede Phase muss als eigenständig wertvolles Release stehen können. Wenn Phase 1 fertig ist und niemand sie benutzt, muss das Konzept hinterfragt werden – nicht Phase 2 angefangen.
- **Performance-Disziplin.** Bei jedem neuen Feature die Frage stellen: „Was passiert bei 10.000 Commits?" Wenn die Antwort „weiß ich nicht" lautet, gehört das Feature nicht ins Release.
- **Patreon-Versprechen ernst nehmen.** Wenn Preview-Features stabil genug für Patrons sind, müssen sie auch zeitnah zum kostenfreien Release kommen. Sonst entsteht das, wogegen sich Gitster positioniert: ein zweistufiges Modell mit Paywall.
- **Open-Source bleibt open-source.** Das Lizenz-Modell ist Vertrauensvorschuss – einmal gebrochen, ist er weg.
- **Sicherheits-Indikatoren vor Features.** Outgoing/Incoming/Synced-Anzeige und Force-Push-Warnungen kommen *vor* neuen Komfort-Features. Ein Tool, das History umschreibt, muss zeigen wann das gefährlich wird — und das in jeder Ansicht, in der ein Commit auftaucht.
- **Strukturelle Refactorings vor Features.** Phase 2a (Modus-Sidebar) kommt vor Phase 2b (Stash-Killer). Architektur-Arbeit, die später teuer wird, wird früh gemacht.