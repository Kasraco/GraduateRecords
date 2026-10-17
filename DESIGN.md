# DESIGN.md: UI/UX Design System

Stack: ASP.NET Core MVC · Razor · **Tailwind CSS v4** · shadcn-inspired Razor components · vanilla JS (ES modules). **No React.** Persian-first, RTL-first. Style: calm, professional, high clarity. No decorative gradients, heavy shadows, or gratuitous animation.
Rules in `AGENTS.md §18/§21` apply. If this file conflicts with `ui-ux-pro-max` output, this file wins.

## 0. Using `ui-ux-pro-max`

Run the installed `ui-ux-pro-max` skill FIRST for **significant UI work**:
- dashboards (student, staff), the Form Designer, the student form-filling experience, review pages, major layouts/navigation, workflow visualization, complex responsive components, and page-level loading/empty/error design.

Do NOT run it for trivial changes: adding an input or button, copy edits, a new column in an existing table, reusing existing components with different data.

Then: treat output as guidance and adapt it to Razor + Tailwind v4 + this document; never introduce React or a client framework; reuse existing components (`UI/TagHelpers`, `UI/ViewComponents`, `Views/Shared/Ui`) before creating new ones; RTL/Persian rules always apply; validate per Master Instructions §6.

## 1. Principles
1. Clarity over style, with one primary action per region. 2. Irreversible actions are visually distinct and confirmed. 3. Status is always badge + text, never color alone. 4. Efficiency for staff, reassurance for students. 5. Server-first: pages work with plain navigation, JS enhances. 6. Same pattern → same component.

## 2. Layout
- `<html lang="fa" dir="rtl">`. Max width: 1280 px (staff/admin), 720 px (student form pages), 960 px (student dashboard).
- **Staff/Admin shell:** sidebar on the start (right) edge (264 px, collapsible; drawer below `lg`), top bar (global student search, user menu), page header (breadcrumbs, title, actions).
- **Student shell:** top bar + **bottom navigation on mobile** (داشبورد · فرم‌ها · درخواست‌های من). On form-filling pages the bottom nav yields to the sticky action bar.
- Breakpoints: Tailwind defaults (`sm 640, md 768, lg 1024, xl 1280`). Design student screens at 360 px first.
- Page order: breadcrumbs → header (title + primary action at the end edge) → filters → content → pagination.

## 3. Navigation
- Staff: داشبورد، صف بررسی، دانشجویان، گزارش‌ها. Admin adds: فرم‌ها، گردش‌کارها، کاربران و نقش‌ها، داده‌های پایه (only if ADR-0012 allows local management)، گزارش ممیزی.
- Items filtered by permission **for UX only**. The server enforces access.
- Active item: `bg-accent` + `aria-current="page"`. Breadcrumbs on pages deeper than level 1 with mirrored separators.

## 4. Typography
- **Vazirmatn**, self-hosted woff2, `font-display: swap`, fallback `Tahoma, system-ui, sans-serif`. Tabular numerals in tables and IDs.
- Scale: page title `text-2xl font-bold`; section `text-lg font-semibold`; body `text-base leading-7`; dense/table `text-sm leading-6`; caption `text-xs`. Minimum 14 px for interactive labels.
- Persian: no letter-spacing, no italics, no uppercase logic, weights 400/500/700 only, keep ZWNJ, reading line length ≤ 70 characters.
- Digits: Persian digits in display text and dates; identifiers use `dir="ltr"` + tabular numerals.

## 5. Spacing, radius, shadows, borders
4 px grid. Inputs `px-3 py-2` (student `min-h-11`), cards `p-4 md:p-6`, gutter `px-4 md:px-6`, sections `space-y-6`. Radius: `--radius: 0.5rem` for inputs/buttons, `rounded-xl` cards, `rounded-full` badges. 1 px borders, `shadow-xs` on cards, `shadow-md` on popovers/dialogs.

## 6. Tailwind v4 setup and tokens

### 6.1 Version and syntax rules (frozen)
- Tailwind **v4.x**. `package.json` pins exact versions of `tailwindcss` and `@tailwindcss/cli` (lockfile committed). Upgrading the major version requires an ADR.
- No `tailwind.config.js`. Configuration lives in CSS. Scripts: `build:css` = `tailwindcss -i ./Assets/css/app.css -o ./wwwroot/css/app.css --minify`; `watch:css` = same with `--watch`. The output is generated and gitignored. CI builds it.
- v4 names to use: `shadow-xs` (subtle), `shadow-sm`, `rounded-xs`, `outline-hidden` (not `outline-none`) when removing the default outline together with a visible `focus-visible:ring-2`, explicit `ring-2` (default ring is 1 px), color opacity via `bg-primary/90` (no `bg-opacity-*`), important via trailing `!`. Default border color is `currentColor`, so always use `border-border` or the base-layer rule below.
- Browser support: v4 targets modern browsers (Chrome 111+, Safari 16.4+, Firefox 128+). The university's browser matrix is confirmed in ADR-0011 before Phase 1D UI work.

### 6.2 `Assets/css/app.css` skeleton
```css
@import "tailwindcss";
@source "../../Views";
@source "../../Areas";
@source "../../UI";            /* C# TagHelpers contain class strings */
@source "../../wwwroot/js";

:root {
  --radius: 0.5rem;
  --background: #F8FAFC;  --foreground: #0F172A;
  --card: #FFFFFF;        --card-foreground: #0F172A;
  --primary: #1E40AF;     --primary-foreground: #FFFFFF;
  --secondary: #F1F5F9;   --secondary-foreground: #0F172A;
  --muted: #F1F5F9;       --muted-foreground: #475569;
  --accent: #E0E7FF;      --accent-foreground: #1E3A8A;
  --destructive: #B91C1C; --destructive-foreground: #FFFFFF;
  --success: #15803D;     --success-soft: #DCFCE7;
  --warning: #B45309;     --warning-soft: #FEF3C7;
  --info: #0369A1;        --info-soft: #E0F2FE;
  --border: #CBD5E1;      --input: #94A3B8;      --ring: #1E40AF;
}
@theme inline {
  --font-sans: "Vazirmatn", Tahoma, system-ui, sans-serif;
  --color-background: var(--background);  --color-foreground: var(--foreground);
  --color-card: var(--card);              --color-card-foreground: var(--card-foreground);
  --color-primary: var(--primary);        --color-primary-foreground: var(--primary-foreground);
  --color-secondary: var(--secondary);    --color-secondary-foreground: var(--secondary-foreground);
  --color-muted: var(--muted);            --color-muted-foreground: var(--muted-foreground);
  --color-accent: var(--accent);          --color-accent-foreground: var(--accent-foreground);
  --color-destructive: var(--destructive);--color-destructive-foreground: var(--destructive-foreground);
  --color-success: var(--success);        --color-success-soft: var(--success-soft);
  --color-warning: var(--warning);        --color-warning-soft: var(--warning-soft);
  --color-info: var(--info);              --color-info-soft: var(--info-soft);
  --color-border: var(--border);          --color-input: var(--input);   --color-ring: var(--ring);
  --radius-lg: var(--radius);
}
@layer base {
  *, ::before, ::after { border-color: var(--border); }
  html { font-family: var(--font-sans); }
  body { background: var(--background); color: var(--foreground); }
}
@font-face { font-family: "Vazirmatn"; src: url("/fonts/vazirmatn/Vazirmatn-Variable.woff2") format("woff2"); font-display: swap; }
```
Components use tokens, never raw hex. Contrast ≥ 4.5:1 for text and ≥ 3:1 for UI boundaries (verify when changing tokens). Dark mode is out of scope, but the token structure allows it later.

### 6.3 Submission/workflow runtime status mapping (`StatusBadge` ViewComponent)
| Status | Persian | Style |
|---|---|---|
| Draft | پیش‌نویس | muted |
| Submitted | ارسال‌شده | info-soft |
| UnderReview | در حال بررسی | info |
| NeedsCorrection | نیازمند اصلاح | warning |
| Approved | تأییدشده | success |
| Rejected | ردشده | destructive (soft) |

This mapping covers submission/workflow runtime status only. `FormVersion` (Draft/Published/Retired) and `WorkflowDefinitionRevision` statuses are not defined by this table. Define their presentation separately when first needed. A no-workflow submission only ever shows `Draft` or `Submitted`.

## 7. Canonical Persian terminology
ذخیره پیش‌نویس (Save Draft) · ارسال نهایی (Final Submit) · بازگشت برای اصلاح (Return for Correction) · ارسال مجدد (Resubmit) · تأیید (Approve) · رد (Reject) · شروع بررسی (Start Review) · رها کردن بررسی (Release, only if claiming is kept) · انتشار (Publish) · پیش‌نمایش (Preview) · نسخه (Version) · درخواست (Submission) · مدرک/پیوست (Document) · انصراف (Cancel) · ذخیره (Save) · حذف (Delete) · جستجو (Search). Use exactly these terms.

## 8. Components (shadcn-inspired)
All components: tokens only, logical utilities only, `focus-visible:ring-2 ring-ring ring-offset-2`, disabled = `opacity-50 cursor-not-allowed` + `aria-disabled`.
- **Button:** `primary, secondary, outline, ghost, destructive, link`; sizes `sm h-8 / md h-10 / lg h-11`; icon on the start edge; spinner replaces icon when loading; submit buttons disable on submit (UX only).
- **Input/Textarea:** `h-10` (student `h-11`), border `input`, label above, help below, error below in `destructive` with an icon. LTR fields: `dir="ltr" class="text-end"`.
- **Select:** native `<select>` styled. Searchable combobox (JS) only for long lists.
- **Checkbox/Radio:** native inputs with `accent-primary`, 20 px hit area (24 px on student), clickable label.
- **Card:** `rounded-xl border bg-card shadow-xs`. **Badge:** pill, soft background + dark text. **Alert:** info/success/warning/destructive with icon + title; `role="alert"` for warning/destructive.
- **Dialog:** native `<dialog>` + small JS (`showModal()` focus trap, ESC, focus return). Mobile: bottom-sheet style. Destructive confirm button on the end edge, cancel on the start edge.
- **Dropdown:** `<button aria-haspopup>` + `role="menu"`, arrow keys, ESC.
- **Tabs:** `role="tablist"`; arrow keys follow DOM order in RTL (test with a keyboard); `?tab=` persistence.
- **Table:** sticky header, `text-sm`, hover row, status badge column, actions at the end edge, `tabular-nums`, `aria-sort`. Mobile: student tables become stacked cards; staff tables scroll in `overflow-x-auto` with a sticky first column.
- **Pagination:** page size 20 (20/50/100), «صفحه ۲ از ۸ — ۱۶۲ مورد», mirrored prev/next icons, page in query string.
- **Breadcrumbs:** `nav aria-label="مسیر صفحه"`. **Toast:** top-center (mobile), top-start (desktop), 5 s auto-dismiss (errors persist), `aria-live`, fed by TempData after PRG.
- **Skeleton:** only for async regions; `motion-reduce` disables pulse. **Empty state:** icon + one sentence + one primary action.
- **Error states:** inline field errors; form error summary (focus moves there, links to fields); 403/404/500 pages with correlation id: `خطایی رخ داده است.` / `کد پیگیری: …`; API-unavailable warning banner that never blocks the page.
- **Icons:** Lucide-derived SVG sprite via `<ui-icon name>`. Directional icons get `rtl:-scale-x-100`; others do not.
- **Dynamic text rule (JS):** components built in JS use `textContent`/DOM APIs only; never `innerHTML` for labels, options, answers, filenames or comments.

## 9. Forms and validation UX
Single column; label above control; required marked `*` + hidden "(اجباری)" text; optional fields unmarked. Validate on submit, then on blur after the first failure. Specific Persian messages. Error summary + inline errors + `aria-invalid` + `aria-describedby`. Preserve input after failed validation. Correct `inputmode`, `autocomplete`, `enterkeyhint`.

## 10. Jalali date UX
One text input with mask `۱۴۰۵/۰۷/۲۳` (accepts Persian or ASCII digits) + calendar button opening a Jalali picker (Saturday start, Persian month names, keyboard operable, vendored, no CDN, no jQuery; library chosen by ADR-0009). The server parses the visible text (single source of truth; no hidden Gregorian field). Read-only: short `۱۴۰۵/۰۷/۲۳`, long `۲۳ مهر ۱۴۰۵`, date-time `ساعت ۱۴:۳۰` Tehran. Errors: «تاریخ نامعتبر است»; range errors state the Jalali range.

## 11. RTL/LTR mixed content
Logical utilities only. Review checklist: no `ml- mr- pl- pr- left- right- text-left text-right float-*`. LTR data: `dir="ltr"` + `text-end`. Free text/names inside sentences: `<bdi>`. Mirror directional icons. Phone numbers, IDs and codes render left-to-right inside RTL layout.

## 12. Accessibility (WCAG 2.1 AA)
Landmarks, skip link («پرش به محتوا»), one `h1`, labelled controls, visible focus, full keyboard operation (dialogs, menus, tabs, designer), ≥ 44 px targets on student screens, status never by color alone, `aria-live` for toasts and designer announcements, `prefers-reduced-motion`, 200 % zoom without loss.

## 13. Responsive behavior
- Student: mobile-first single column; sticky bottom action bar on forms; cards instead of tables; full-width buttons on `<sm`; camera/gallery upload via file input.
- Staff/Admin: desktop-first; sidebar → drawer `<lg`; tables scroll; review page stacks and the decision panel becomes a sticky bottom bar `<lg`.
- Form Designer: three panes at `≥ xl`; two panes + drawers at `md–lg`; on phones a read-only preview with the notice «برای ویرایش فرم از رایانه یا تبلت استفاده کنید».

## 14. shadcn adaptation (no React)
shadcn is a **pattern source** (tokens, variants, anatomy, accessibility behavior), not a dependency.

| Need | Mechanism | Examples |
|---|---|---|
| Primitive with variants/sizes | **TagHelper** (`UI/TagHelpers`) | `<ui-button>`, `<ui-badge>`, `<ui-alert>`, `<ui-icon>`, `<ui-field asp-for>` |
| Static composite markup | **Razor partial** (`Views/Shared/Ui`) | `_CardHeader`, `_EmptyState`, `_FormActions` |
| Needs data/logic | **ViewComponent** | `StatusBadge`, `Pagination`, `Breadcrumbs`, `PageHeader`, `DynamicFormRenderer`, `WorkflowTimeline` |
| Interactive behavior | **Vanilla JS module** bound via `data-ui="dialog|dropdown|tabs|combobox|datepicker|toast"` | native `<dialog>`, menus, tabs |

Rules: variant → class maps live in C# (one place per component). There is no class-merge library in .NET, so components accept only non-conflicting extra classes (layout/margin). Build the base primitives up front (button, badge, alert, card, field/input/select/textarea/checkbox/radio, page header, empty state, status badge, pagination, dialog, toast, icon). Extract everything else after the second real use. No generators, no Storybook. A dev-only `/Admin/UiKit` page lists components.
**`DynamicFormRenderer`** is one ViewComponent with modes `Edit` (student), `Preview` (designer, sample data), and `ReadOnly` (review page, revision view). Phase ownership: **Phase 2** implements and validates `Preview` and establishes the reusable renderer contract. **Phase 3** enables `Edit` (student). **Phase 4** enables `ReadOnly` (staff review). Do not pull Phase 3/4 business behavior into Phase 2.

## 15. Form Designer UX (Admin)

v1 scope: add field, select field, edit properties, move up/down, delete + undo, validation settings, AutoFill, preview, save draft, publish, version visibility. **Not in v1:** drag-and-drop requirement, conditional visibility, calculated fields, repeating sections, expressions, scripting. The three-pane layout is a recommendation. A click/keyboard-only flow must always work.

Desktop layout (RTL: palette on the right/start, properties on the left/end):
```text
┌────────────────────────────────────────────────────────────────────────────┐
│ طراح فرم: «درخواست تصویب موضوع» · نسخه ۳ [پیش‌نویس]  ○ ذخیره‌نشده         │
│                        [نسخه‌ها] [پیش‌نمایش] [ذخیره پیش‌نویس] [انتشار…]    │
├────────────────┬──────────────────────────────────────┬────────────────────┤
│ Properties     │ Form Canvas                          │ Field Types        │
│ [عمومی][اعتبار │  ┌──────────────────────────────┐    │ + متن کوتاه        │
│  سنجی][پر شدن  │  │ عنوان پایان‌نامه *   ↑ ↓ 🗑  │    │ + متن بلند         │
│  خودکار][گزینه │  │ [______________________]     │    │ + عدد              │
│  ها]           │  └──────────────────────────────┘    │ + تاریخ            │
│ برچسب …        │  [+ افزودن فیلد]                     │ + لیست کشویی       │
│ کلید: …🔒      │                                      │ + گزینه‌های رادیویی│
│                │                                      │ + تیک · + فایل     │
│                │                                      │ + اطلاعات دانشجو   │
└────────────────┴──────────────────────────────────────┴────────────────────┘
```
(No professor field type in v1. Use «متن کوتاه» for names.)

Interaction model: vanilla JS module `features/form-designer` keeps an in-memory draft model. The server is the source of truth and re-validates everything.
- **Add:** click/Enter on a palette type inserts after the selected field (or at the end), selects it, and focuses the label input. «+ افزودن فیلد» opens the same type picker (tablet/mobile).
- **Select:** click/Enter highlights with a ring and populates properties. `Esc` deselects. ↑/↓ move the selection.
- **Edit properties (tabs):** *عمومی* (label, help text, placeholder, required, reportable, sensitive; reportable and sensitive are mutually exclusive); *اعتبارسنجی* (type-dependent: length, value, date range, named pattern, file types/size/count); *پر شدن خودکار* (source dropdown limited to `AutoFillSource`, **mode selector**: «فقط نمایش»، «مقدار اولیه (قابل ویرایش)»، «ثبت ثابت»; only valid mode/type combinations offered; selecting شماره ملی defaults to «فقط نمایش»; choosing «ثبت ثابت» for it requires `forms.sensitive-snapshot`, forces the sensitive flag, and shows a warning); *گزینه‌ها* (Select/Radio: add/remove/reorder, option codes auto-generated). Changes apply live on the canvas. All labels/options are rendered as plain text (`textContent`).
- **Key:** auto-generated ASCII key, editable until first publish; locked (🔒 + tooltip) when inherited from a published version.
- **Reorder:** move-up/move-down buttons and `Alt+↑/↓`; `aria-live` announcement «فیلد … به جایگاه ۳ منتقل شد».
- **Delete:** trash button, removed immediately with an **undo toast** (8 s). Only draft-added or draft-inherited fields may be deleted. Key reuse is blocked for keys that exist in earlier published versions.
- **Validation settings:** inline hints (min > max); server errors map back to the field and a summary.
- **Preview:** full-screen dialog with `DynamicFormRenderer` in Preview mode, sample student data, phone/tablet/desktop width toggle; nothing saved.
- **Save draft:** button + `Ctrl+S`; whole model sent, the server replaces draft fields transactionally with `RowVersion` concurrency. Indicator «ذخیره‌نشده» / «ذخیره شد ۱۰:۲۵». `beforeunload` warns when dirty. Conflict → dialog offering reload.
- **Publish:** dialog summarizing field count, required count, diff vs previous version, warnings (sensitive snapshot; if a workflow is bound but has no Published revision, final submit will fail until one is published. This is a warning, never a publish blocker), and «پس از انتشار، این نسخه قابل ویرایش نیست.» Confirm button «انتشار نسخه ۳». Invalid → blocked with an error list. The designer has no workflow-binding control until Phase 4. Forms published earlier have no workflow. The Phase 4 workflow-binding control exists only on Draft versions. A published version shows it read-only. Changing the binding means «ایجاد پیش‌نویس از نسخه N».
- **Version visibility:** header shows version + status badge; «نسخه‌ها» drawer lists versions (status, date/by, submission count, Retired marker) with «ایجاد پیش‌نویس از نسخه N» and read-only viewing. When editing a derived draft: info alert «N درخواست با نسخه قبلی ثبت شده‌اند و تغییر نخواهند کرد.»
- States: loading skeleton; empty canvas «هنوز فیلدی اضافه نشده. یک نوع فیلد را از فهرست انتخاب کنید»; save-failure banner with retry.

## 16. Student UX
- **Dashboard:** greeting; sync banner only when stale (`آخرین همگام‌سازی: …`, never blocking); sections «نیازمند اقدام» (corrections + drafts first), «فرم‌های در دسترس» (cards: title, description, deadline, «شروع»), «درخواست‌های اخیر» (badge + last update). Empty state for each.
- **Available forms:** only eligible and open forms (server decides). If ADR-0005 blocks starting because of expired data, show a clear explanation, not a silent absence.
- **Form filling:** single column; DisplayOnly/Snapshot AutoFill fields are read-only with a lock icon and «از اطلاعات دانشگاهی شما پر شده است»; InitialValue fields look like normal editable fields with the hint «مقدار اولیه از اطلاعات دانشگاهی؛ می‌توانید ویرایش کنید».
- **Save Draft vs Final Submit (must be unmistakable):** sticky action bar (bottom on mobile; end-aligned footer on desktop). «ذخیره پیش‌نویس» = `outline` + save icon + helper «می‌توانید بعداً ادامه دهید؛ برای بررسی ارسال نمی‌شود». «ارسال نهایی» = solid `primary` + send icon, **always** preceded by a «بازبینی و ارسال» step summarizing answers, listing validation errors, and warning «پس از ارسال نهایی تا زمان درخواست اصلاح امکان ویرایش ندارید» (for forms without a workflow: «پس از ارسال نهایی امکان ویرایش ندارید»). Status line «پیش‌نویس · آخرین ذخیره ۱۰:۲۵» (autosave 30 s after last change, drafts and NeedsCorrection only; autosave failure → warning, not an error page; version conflict → reload dialog). The submit form carries the server-issued `OperationId`.
- **Documents:** per-field upload area with accepted types/size, progress, per-file error, remove before submit, mobile camera/gallery. The file uploads immediately and independently of the form (generic upload). Attaching it to the field is a separate step. Uploading never submits. Save/final submit only verifies the attachments and finalizes their linkage; it never re-uploads.
- **Correction requests:** warning alert with the reviewer comment, button «ارسال مجدد» (also behind a review step).
- **History/status:** list with badges; detail with `WorkflowTimeline` (events and comments visible to the student) for workflow-enabled submissions.
- **Mobile:** 360 px first, 44 px targets, bottom nav, cards instead of tables, no horizontal scroll, `inputmode`/`autocomplete`, sticky action bar never covers inputs (scroll-padding).

## 17. Staff UX
- **Dashboard:** counters (در انتظار بررسی، در حال بررسی من، نیازمند اصلاح، تأییدشده این ماه), «قدیمی‌ترین در انتظار», each counter linking to the pre-filtered queue. All data respects staff scope.
- **Review queue (workflow-enabled submissions only):** tabs («در انتظار» · «در حال بررسی من» · «همه»), filters (form, department, program, Jalali date range, assignee, text), filters in the query string, oldest-first default, row click opens review, optional «شروع بررسی» row action.
- **Student search:** top-bar search (student number exact, normalized name, National ID exact). Profile tabs: اطلاعات · درخواست‌ها · مدارک. Profile view is audited. National ID masked (`۰۰۱****۸۹`) unless `students.view-national-id` (reveal audited). Staff reach no-workflow submissions here and in reports.
- **Submission review:** two regions, answers (read-only via `DynamicFormRenderer` ReadOnly, grouped, labels from the pinned version, sync timestamp note, DisplayOnly AutoFill values shown from the profile with a "current profile" note) and attachments (inline preview for PDF/image via the authorized endpoint). A sticky **decision panel** has a comment field and three buttons: «تأیید» (primary), «بازگشت برای اصلاح» (warning outline), «رد» (destructive outline). Reject/Return require a comment (inline validation). Each decision has a compact confirm popover (2 clicks). Decision forms carry the server-issued `OperationId`. After a decision, «تأیید و مورد بعدی» moves to the next queue item. If claiming is kept: opening shows «شروع بررسی» and, once claimed by someone else, a read-only notice.
- **Workflow history:** timeline (actor display-name snapshot, date, action, comment); earlier submission revisions selectable («ارسال ۱ / ۲») with changed fields highlighted when practical.
- **Reports (Phase 5):** choose form → columns (reportable fields) → filters → preview → Excel/PDF (`reports.export`). Sensitive fields never offered by default.

## 18. Page states checklist (every new page)
Loading · Empty · Validation error · 403 · 404 · 500 with correlation id · API-unavailable banner where relevant · Long Persian text wrapping · Narrow screen · Keyboard-only walk-through · Double-submit behavior.
