# ADR 0002: Visible taskbar monitor host

Date: 2026-10-05. Status: accepted.

The current Explorer composition bridge covered a foreign WPF child surface: the child was visible, all six text layouts were readable through UI Automation, but a physical taskbar capture contained no monitor pixels. Software rendering alone did not fix the child host. An independent software-rendered popup produced visible text in the same safe gap.

Constraints: keep app/start/notification buttons usable; no Explorer process injection or system taskbar modification; no activation or Alt-Tab entry; maintain settings and updater compatibility.

| Option | Decision |
|---|---|
| Hardware-rendered foreign child | Reject: reproduced invisible surface |
| Software-rendered foreign child | Reject: still covered in actual capture |
| Native GDI foreign child | Reject: keeps the same unsupported composition relationship |
| Inject into Explorer | Reject: violates process-isolation constraint |
| Legacy deskband extension | Reject: incompatible with modern taskbar hosting |
| Desktop widget only | Reject: does not satisfy taskbar display requirement |
| Independent software-rendered popup | Accept: visible actual pixels and working right-click on this machine |

The popup uses physical screen coordinates, NOACTIVATE, TOOLWINDOW and TOPMOST. Native shell bounds supplement automation. Own controls/events are excluded. It hides while the taskbar is partially offscreen, so auto-hide animation can never leave a strip floating on the desktop. It resumes when fully visible. Geometry checks run independently of metrics sampling. This deliberately trades text visibility during the slide for reliable stationary rendering. Vertical taskbars and insufficient safe space remain unsupported; the tray/settings panel stays available.

Acceptance evidence: actual desktop crop showed all six metrics without covering Start/app icons; actual right-click on Today opened Traffic and on Upload opened Monitor. Synthetic messages were not accepted as evidence because WPF hit testing depends on real cursor position.
