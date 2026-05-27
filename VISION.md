# Gitster – Vision

## Was ist Gitster?

Gitster ist kein weiterer vollständiger Git-Client. Es ist ein **chirurgisches Werkzeug** für Entwickler, die präzise Kontrolle über Git-Metadaten, History und unkonventionelle Workflows benötigen – Dinge, die in GitHub Desktop, Fork oder GitKraken entweder gar nicht oder viel zu umständlich möglich sind, und für die GitButler den Workflow zu sehr neu erfindet.

**Positionierung:** Gitster ergänzt einen normalen Git-Workflow. Es ist das Tool, das man aufmacht, wenn man eine präzise History-Operation braucht – nicht das, in dem man den ganzen Tag arbeitet.

---

## Zielgruppe

Entwickler, die Git gut kennen, aber bestimmte Metadaten- und History-Operationen regelmäßig brauchen und keine Lust haben, dafür jedes Mal komplexe `git rebase -i`- oder `git log -S`-Befehle zu schreiben.

---

## Was Gitster nicht werden soll

- Kein vollständiger Merge-Konflikt-Editor
- Kein Ersatz für `git log --graph` mit hübschen Linien
- Keine Cloud-Integration, keine Issue-Tracker-Anbindung, kein PR-Workflow
- Keine KI-Features (keine generierten Commit-Messages, keine Branch-Namen-Vorschläge, kein Agent-Modus)
- Keine Repository-Verwaltung (Clone, Init, Repository-Settings jenseits von Config-Healthcheck)
- Kein Datei-Browser, keine Commit-Authoring-Hilfe für *neue* Commits – Gitster ist ein Tool für *bestehende* History

---

## Phase 0 – Bereits umgesetzt

- **Commit-Zeitstempel ändern** – Datum und Uhrzeit des letzten Commits nachträglich anpassen (Author + Committer)
- **Zeit aus Commit übernehmen** – Zeitstempel eines beliebigen Commits in den Datepicker laden
- **Remote-Operationen** – Fetch, Pull, Push, Sync mit Remote-Auswahl direkt in der UI
- **Commit-Liste mit Filter** – Commits nach Autor und weiteren Kriterien filtern
- **Eigener DateControl** – Komfortabler Datums-/Zeitpicker mit Tastaturkürzeln und Kalender-Popup

---

## Phase 1 (Monate 1–3) – Fundament: Sicherheitsnetz & Metadaten-Chirurgie

Bevor Gitster destruktive Operationen über mehrere Commits hinweg anbietet, braucht es ein robustes Undo. Diese Phase legt das Fundament für alles Weitere und liefert gleichzeitig die ersten beiden „Killer-Features" über die Einzel-Commit-Bearbeitung hinaus.

- **Operations-Log** – Jede destruktive Aktion (Amend, Rebase, Squash, Reset) wird als rückgängig machbarer Eintrag protokolliert. Intern Reflog-basiert, aber als browsable, beschriftete Liste exponiert.
- **Undo** – Letzten Vorgang rückgängig machen, auch nach Amend oder Rebase. Mehrstufig, nicht nur eine Ebene tief.
- **Zustandsanzeige vor jeder Operation** – Klarer Hinweis, was sich ändern wird: betroffene Commits, SHA-Vorschau vorher/nachher.
- **Mehrere Commits umschreiben** – Zeitstempel einer Commit-Range per Rebase automatisch anpassen (z. B. „alle Commits in diesem Branch auf Arbeitsstunden verschieben")
- **Autor reparieren** – Falschen Namen/E-Mail über mehrere Commits hinweg korrigieren, mit Vorschau welche Commits betroffen sind
- **Sicherer Force-Push** – Alle Push-Operationen nach Amend/Rebase verwenden intern `--force-with-lease` statt blindem `--force`
- **Git-Config Gesundheitscheck** – Einmalig prüfen, ob sinnvolle Einstellungen fehlen (`diff.algorithm histogram`, `fetch.prune true`, `rerere.enabled`, `push.autoSetupRemote`, `branch.sort -committerdate`) und per Klick aktivieren

---

## Phase 2 (Monate 4–6) – Fixup-Workflow & Commit-Message-Chirurgie

Git hat `git commit --fixup=[sha]` + `git rebase --autosquash`, aber kaum jemand kennt es – und niemand will SHA-Hashes tippen. Diese Phase macht das Bearbeiten *bestehender* Commits zur Standardoperation.

- **Fixup per Klick** – Staged Changes einem beliebigen Commit aus der Liste zuweisen (kein SHA-Tippen); Gitster führt intern `--fixup` + `--autosquash` aus
- **Reword beliebiger Commit** – Commit-Message irgendwo in der History direkt im UI ändern, nicht nur HEAD, via `git commit --fixup:reword=[sha]`
- **Autosquash-Vorschau** – Vor dem Ausführen anzeigen, welche Commits in welcher Reihenfolge zusammengeführt werden
- **Squash mit Datumskontrolle** – Commits zusammenführen und gezielt wählen, welches Datum und welche Message behalten werden
- **Cherry-pick mit Zeitstempel** – Commit aus einem anderen Branch übernehmen, dabei Datum überschreiben

---

## Phase 3 (Monate 7–9) – Branch-Operationen ohne Kontextwechsel

Das größte Reibungsfeld im Alltag: Du arbeitest an etwas, merkst „Mist, das gehört eigentlich auf einen anderen Branch", und musst stashen, switchen, applyen. Diese Phase eliminiert den Kontextwechsel.

- **Commit auf anderen Branch** – Staged/unstaged Dateien direkt in einen anderen Branch committen, ohne `stash` oder `switch` – der aktuelle Workingstate bleibt unangetastet
- **Branch-Snapshot** – Aktuellen Stand als neuen Branch sichern (leichtgewichtiger als Stash, benannter als Stash)
- **Worktrees als First-Class-Citizen** – Worktrees anlegen, wechseln, im Dateisystem öffnen, verwaiste aufräumen. Die richtige Antwort auf „ich muss schnell auf einem anderen Branch arbeiten", aber heute praktisch unbenutzbar wegen CLI-Friktion.
- **Branch-Liste nach Datum** – Branches sortiert nach letztem Commit-Datum statt alphabetisch (niemand will `feature/xyz-old` ganz oben sehen)
- **Stash-Killer** – Alle Stashes als benannte, durchsuchbare Liste mit Diff-Vorschau. Auto-Naming (`stash@{2}` → „wip: 3 files in src/auth, 2h ago"). „In Branch umwandeln"-Aktion mit einem Klick.

---

## Phase 4 (Monate 10–12) – Suche & Analyse

Die mächtigsten Git-Features sind alle CLI-only und kaum bekannt. Diese Phase macht sie zugänglich – das sind die Features, die kein anderes GUI hat.

- **Pickaxe-Suche** – `git log -S "string"` als GUI: Finde jeden Commit in der gesamten History, bei dem ein bestimmter String hinzugefügt oder entfernt wurde. Ideal für Security-Audits, Debugging, „wann wurde diese Funktion gelöscht?"
- **Diff-Suche mit Regex** – `git log -G "regex"` als GUI: Alle Commits anzeigen, die einen Pattern im Diff enthalten
- **Blame mit Code-Verfolgung** – `git blame -w -C -C -C` als Ansicht: Blame, der Whitespace-Änderungen ignoriert und Code durch Umbenennungen/Refactorings verfolgt. Kein einziges GUI implementiert das ordentlich.
- **Range-diff-Visualizer** – `git range-diff` als GUI: Nach einem Rebase sehen, was sich an den eigenen Commits *außer* dem Parent geändert hat. Unverzichtbar vor Force-Push und für Patch-basiertes Review.
- **Diff-Comparison zwischen beliebigen Refs** – Klar gekennzeichneter Drei-Punkte- vs. Zwei-Punkte-Diff (`A...B` vs. `A..B`), den heute niemand korrekt erklärt bekommt
- **Diff-Ansicht des ausgewählten Commits** – Kompakte Anzeige der Änderungen eines ausgewählten Commits direkt in Gitster (Grundlage für die Suche-Ergebnisse)

---

## Phase 5 (Monate 13–15) – Commit-Chirurgie

Die komplexesten Operationen kommen zuletzt, weil sie das Sicherheitsnetz aus Phase 1 und die Diff-Infrastruktur aus Phase 4 brauchen. Hier orientiert sich Gitster bewusst an GitButler – aber als gezielte Operation, nicht als ständiger Workflow.

- **Commits umsortieren** – Drag-and-drop-Reihenfolge von Commits im aktuellen Branch ändern, ohne `rebase -i` anzufassen
- **Commits aufteilen** – Einen Commit in mehrere kleinere aufbrechen, mit Auswahl, welche Dateien/Hunks in welchen neuen Commit wandern
- **Commits zusammenführen** – Mehrere Commits per Auswahl squashen, mit Kontrolle über Datum und Message des Ergebnis-Commits
- **Orphan-Branch erstellen** – Neuen Branch ohne History starten, direkt aus der UI
- **Detached HEAD aufräumen** – Übersichtliche Verwaltung von detached-HEAD-Zuständen mit „in Branch retten"-Aktion

---

## Backlog

Ideen, die zum Profil passen, aber nicht in den ersten 15 Monaten priorisiert sind. Reihenfolge ist hier bewusst nicht festgelegt – das wird nach Nutzer-Feedback aus den ersten Phasen entschieden.

### Komfort & Workflow
- **Zeitstempel-Vorlagen** – Gespeicherte Zeit-Presets („gestern 09:00", „letzten Freitag 17:30") für schnelles Befüllen
- **Mehrfach-Amend-Queue** – Mehrere Commits in einer Session mit verschiedenen Zeiten amenden, als Batch ausführen

### Erweiterte Repo-Hygiene
- **Submodul-Status auf einen Blick** – Kompakte Liste „diese Submodule sind dirty, diese hinken hinterher" – heute nirgends sichtbar
- **Rerere-Visualizer** – Liste der gespeicherten Konflikt-Auflösungen, inspizierbar und löschbar
- **Hook-Editor** – `.git/hooks/`-Skripte editieren, testen, bekannte Snippets anbieten („verhindere Commits in main", „verhindere große Dateien")
- **Reflog-Browser** – Den Reflog als brauchbare, durchsuchbare UI – „zeig mir, wo HEAD vor 3 Stunden war"

### Sicherheit & Signing
- **Signing-Management** – Sichtbar machen, welche Commits signiert sind und mit welchem Schlüssel; Signaturen beim Amend nachträglich hinzufügen (GPG/SSH/gitsign)

### Power-User-Nischen
- **Bundle-URI / Partial-Clone-Verwaltung** – Neue Git-Features (`--filter=blob:none`, bundle-uri), die noch kein GUI exponiert

---

## Risiken & Disziplin

- **Feature-Creep ist die größte Gefahr.** Jede Phase muss als eigenständig wertvolles Release stehen können. Wenn Phase 1 fertig ist und niemand sie benutzt, muss das Konzept hinterfragt werden – nicht Phase 2 angefangen.
- **Die Zeitstempel-Manipulation wird im Marketing bewusst nicht als Hero-Feature inszeniert.** Sie ist ein Werkzeug unter vielen, weil sie sonst die falsche Zielgruppe anzieht (Contribution-Faker statt Profis). Hero-Narrativ ist *Metadaten-Chirurgie* und *Suche/Analyse, die kein anderes GUI hat*.
- **Kein direkter Wettbewerb mit GitButler.** GitButler erfindet den Workflow mit Virtual Branches neu. Gitster ergänzt den klassischen Git-Workflow um Operationen, die in der CLI weh tun. Beide können nebeneinander existieren.