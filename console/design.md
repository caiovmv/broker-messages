# Console design

The console uses one system: a near-black paper, orange action, Inter, a left rail, and rounded surface cards. It does not invent a second palette or a second symbol.

## Who may enter

The console has one operator. The user and password come from configuration. That person sees applications, destinations, domains, deliveries, and health, and can write applications and destinations. There is no second role.

## Breakpoints

Mobile first. The layout changes at 768px and nowhere else.

| Name | Width | Arrangement |
| --- | --- | --- |
| Phone | under 768px | Menu opens the rail as a drawer. Forms are one column. Records are cards. |
| Desk | 768px and up | The rail stays on the left. Page padding grows. |

Do not add another breakpoint. Do not hide primary actions on the phone.

## Layout

- Page background is `--paper` (near black). Surfaces are `--surface` cards with 16px corners. The rail is 248px and stays on the left from 768px. Menu on the phone opens the same rail as a drawer.
- The rail holds the name, the operate group, and the operator at the bottom. Language, theme, and sign-out sit there.
- Navigation groups stay 10px, uppercase, `--muted`. The active word sits on a `--surface` pill with a 3px `--accent` bar on the left.
- Page title is `text-3xl`, semibold, tight tracking. One short lede is allowed under it.

## Color and type

- The first paint is dark. `--paper` is #070707, `--surface` is #141414, ink is #f5f5f5, muted is #8a8a8a. The accent and filled action are orange `#f97316`.
- `html.light` inverts the paper to #f4f4f5 and keeps the same orange action. Theme is stored in `broker.theme`.
- Focus is an `--accent` border. Status dots and meters use `--accent`.
- Type is Inter, then Segoe UI, then the system sans.
- Colors are the variables in `src/index.css`: paper, surface, ink, muted, line, action, danger, warning, accent, rail, and hover.

## Forms

- Every input has a visible label above it. Placeholder text is not a label.
- The primary button uses the verb of the action: create application, add destination, rotate credentials.
- A failure shows the server phrase under the toast. The known phrases are translated.

## Feedback

- Success is a toast. It leaves on its own.
- A failure toast stays until it is dismissed. The text is the server detail, not a generic failure.
- Do not stack a banner and a toast for the same event.

## Loading and empty

- Lists show four quiet skeleton bars while the first request is in flight. Pulse is removed when the user prefers reduced motion.
- An empty list says "Nothing saved yet." Do not draw an empty-state illustration.

## Language

The console speaks Portuguese (`pt-BR`) and English (`en`). The switch sits in the rail footer and is stored in `broker.locale`. The first visit follows the browser language.

New interface copy is added to both catalogs in `src/messages.ts`. A known server phrase is translated at the toast. An unknown phrase is shown as the server sent it.

## Public surfaces

The console opens on operator sign-in. After that, the rail lists applications, destinations, domains, deliveries, and health.

An application card shows the SMTP host, port, user, password, and the API token. Those are the credentials the integrator uses in place of Mailgrid. Destinations are an ordered fallback list per channel. Domains and deliveries are the same records the Mailgrid read endpoints return. Lists keep the skeleton and the empty sentence. Forms keep the label, the error under the field, and the toast.

## Freshness

Lists and figures may come from memory for up to five minutes. A successful save, update, or delete refreshes every open screen that shows that family of records.

The first load, when memory has nothing for that list, uses the four skeleton bars. A later visit that memory can answer paints the records directly, with no second loading treatment and no cached badge.

Do not store API records in `localStorage` or `sessionStorage`. Language and theme stay where they are.

## Motion

- Drawer and menu appear without a long animation.
- Do not animate layout, numbers, or page changes.
