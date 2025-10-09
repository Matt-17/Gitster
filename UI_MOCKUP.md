# Visual UI Mockup - Gitster with Remote Operations

## Complete Window Layout

```
╔═══════════════════════════════════════════════════════════════════════════════════════════════╗
║ Gitster                                                                              [_][□][X] ║
╠═══════════════════════════════════════════════════════════════════════════════════════════════╣
║                                                                                               ║
║  ┌──────────────────────────┐  ┌───────────────────────────────────────────────────────────┐ ║
║  │ Commit List              │  │ Repository Path:                                          │ ║
║  │ ┌──────────────────────┐ │  │ [C:\Users\YourName\Documents\MyRepo                     ] │ ║
║  │ │Message   Date   ID   │ │  │                                                           │ ║
║  │ ├──────────────────────┤ │  │ ╔════════════════════════════════════════════════════════╗ ║
║  │ │Initial   01.01  a1b2c│ │  │ ║ Selected Commit                                        ║ ║
║  │ │➤ Add fe  02.01  d3e4f│ │  │ ║ [Add feature X                       ] [02.01 16:30]  ║ ║
║  │ │Fix bug   03.01  g5h6i│ │  │ ║                                          [🕑]           ║ ║
║  │ │Update    04.01  j7k8l│ │  │ ╚════════════════════════════════════════════════════════╝ ║
║  │ │...                   │ │  │                                                           │ ║
║  │ │                      │ │  │ ╔════════════════════════════════════════════════════════╗ ║
║  │ │                      │ │  │ ║ Current Commit                                         ║ ║
║  │ │                      │ │  │ ║ [Update README.md                    ] [04.01 18:45]  ║ ║
║  │ │                      │ │  │ ║                                          [🕑]           ║ ║
║  │ └──────────────────────┘ │  │ ╚════════════════════════════════════════════════════════╝ ║
║  │                          │  │                                                           │ ║
║  └──────────────────────────┘  │  ┌────────────────────────────────────────────────────┐  │ ║
║                                │  │         Date/Time Sliders (Vertical)               │  │ ║
║                                │  │   Days  Months  Hours  Minutes                     │  │ ║
║                                │  │    ║      ║      ║       ║                         │  │ ║
║                                │  │    ║      ║      ║       ║                         │  │ ║
║                                │  │    ║      ║      ║       ║                         │  │ ║
║                                │  │    ▓      ▓      ▓       ▓                         │  │ ║
║                                │  │    ║      ║      ║       ║                         │  │ ║
║                                │  │    ║      ║      ║       ║                         │  │ ║
║                                │  └────────────────────────────────────────────────────┘  │ ║
║                                │                                                           │ ║
║                                │  [Date: 04/01/2024     ▼] [Time: 18:45]                  │ ║
║                                │                                                           │ ║
║                                │            ┌──────────────────────────────────────────┐  │ ║
║                                │            │ [Fetch] [Pull] [Push] [Sync]             │  │ ║
║                                │            │ [origin        ▼] [Amend]                │  │ ║
║                                │            └──────────────────────────────────────────┘  │ ║
║                                └───────────────────────────────────────────────────────────┘ ║
║                                                                                               ║
╚═══════════════════════════════════════════════════════════════════════════════════════════════╝
```

## Remote Operations Toolbar (Zoomed In)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        Remote Operations Toolbar                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┏━━━━━━━━┓ ┏━━━━━━━━┓ ┏━━━━━━━━┓ ┏━━━━━━━━┓ ┏━━━━━━━━━━━━┓ ┏━━━━━━━━┓  │
│  ┃ Fetch  ┃ ┃  Pull  ┃ ┃  Push  ┃ ┃  Sync  ┃ ┃ origin   ▼ ┃ ┃ Amend  ┃  │
│  ┗━━━━━━━━┛ ┗━━━━━━━━┛ ┗━━━━━━━━┛ ┗━━━━━━━━┛ ┗━━━━━━━━━━━━┛ ┗━━━━━━━━┛  │
│     (1)        (2)        (3)        (4)           (5)           (6)       │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘

Legend:
(1) Fetch  - Download changes without merging
(2) Pull   - Fetch and merge changes
(3) Push   - Upload local changes
(4) Sync   - Full synchronization (fetch + pull + push)
(5) Remote - Dropdown to select remote (origin, upstream, etc.)
(6) Amend  - Amend commit timestamp (original functionality)
```

## Button States

### When Repository is Open and Valid

```
Enabled State:
┏━━━━━━━━┓  All buttons appear solid and clickable
┃ Fetch  ┃  Hover shows tooltip
┗━━━━━━━━┛  Click executes command
```

### When No Repository is Open

```
Disabled State:
┌────────┐  Buttons appear grayed out
│ Fetch  │  Cannot be clicked
└────────┘  No tooltip interaction
```

## Remote Dropdown Behavior

### When Multiple Remotes Exist

```
┏━━━━━━━━━━━━┓
┃ origin   ▼ ┃  Click to open dropdown
┗━━━━━━━━━━━━┛
      ↓
┏━━━━━━━━━━━━┓
┃ origin   ▼ ┃
┠────────────┨
┃ origin     ┃ ← Selected
┃ upstream   ┃
┃ fork       ┃
┗━━━━━━━━━━━━┛
```

### When No Remotes Configured

```
┏━━━━━━━━━━━━┓
┃            ┃  Empty dropdown
┗━━━━━━━━━━━━┛

(Operations will show: "No remote found")
```

## User Interaction Flow

### Successful Fetch Operation

```
Step 1: User clicks [Fetch]
        ┏━━━━━━━━┓
        ┃ Fetch  ┃ ← Click
        ┗━━━━━━━━┛

Step 2: Operation executes
        [Processing...]

Step 3: Success message appears
        ┌─────────────────────────────────────┐
        │  Fetched from origin successfully   │
        │              [ OK ]                  │
        └─────────────────────────────────────┘

Step 4: UI refreshes automatically
        (Commit list updates if new commits fetched)
```

### Error Handling

```
Step 1: User clicks [Push] with authentication issue
        ┏━━━━━━━━┓
        ┃  Push  ┃ ← Click
        ┗━━━━━━━━┛

Step 2: Error detected
        [Processing... Error!]

Step 3: Error message appears
        ┌─────────────────────────────────────┐
        │  Error pushing:                     │
        │  Request failed with status code:   │
        │  401: Unauthorized                  │
        │              [ OK ]                  │
        └─────────────────────────────────────┘

Step 4: No changes to repository
        (User can check credentials and retry)
```

## Tooltip Examples

When hovering over buttons:

```
     ╭────────────────────────────────────────╮
     │ Download changes from remote           │
     │ without merging                        │
     ╰────────────────────────────────────────╯
           ┏━━━━━━━━┓
           ┃ Fetch  ┃ ← Hover here
           ┗━━━━━━━━┛
```

```
     ╭────────────────────────────────────────╮
     │ Synchronize with remote                │
     │ (fetch + pull + push)                  │
     ╰────────────────────────────────────────╯
           ┏━━━━━━━━┓
           ┃  Sync  ┃ ← Hover here
           ┗━━━━━━━━┛
```

## Integration with Existing Features

The new remote operations integrate seamlessly with existing functionality:

- ✓ Maintains existing commit list on the left
- ✓ Preserves commit detail views (Selected and Current)
- ✓ Keeps all date/time manipulation features
- ✓ Retains Amend functionality
- ✓ Follows same MVVM pattern
- ✓ Uses same styling and layout conventions

## Responsive Design

Window width: 1100px (sufficient for all buttons)
- Commit list: 420px (left side)
- Main content: 680px (right side)
- Button toolbar: ~600px (fits comfortably)

All buttons have consistent sizing:
- Operation buttons: 70px × 30px
- Remote dropdown: 120px × 30px
- Margins: 5px between elements
