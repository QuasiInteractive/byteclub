import { useEffect, useState } from "react";
import { fmtDay, fmtTime } from "./api";
import { Glove } from "./Glove";
import { Menu } from "./Menu";
import { sfx } from "./sound";

type Booking = {
  id: string; serviceId: string; service: string; start: string; name: string; phone: string; email?: string;
  suburb: string; address: string; problem?: string; status: string; whatsApp: string;
};
type Enquiry = {
  id: string; serviceId: string; service: string; name: string; phone: string; email?: string;
  idea: string; status: string; createdAt: string; whatsApp: string;
};
type Customer = {
  name: string; phone: string; suburb?: string; visits: number; lastSeen: string; whatsApp: string;
  history: { when: string; what: string; status: string; kind: string }[];
};
type Invoice = {
  id: string; number: string; billToName: string; billToPhone: string; issueDate: string; dueDate: string;
  total: number; status: string; bookingId?: string;
};
type Draft = { bookingId?: string; billToName: string; billToPhone: string; billToEmail: string; lines: { description: string; hours: string; rate: string }[] };
type View = "quests" | "ideas" | "bestiary" | "items";
const money = (n: number) => n.toLocaleString("en-AU", { style: "currency", currency: "AUD" });

const VIEW_HELP: Record<View, string> = {
  quests: "Home visits. Confirm them, mark them done, or cancel to free the time.",
  ideas: "Game and app ideas. Track each one from first message to won or lost.",
  bestiary: "Every customer, matched by phone number, with their history.",
  items: "Invoices. Open the PDF to send it, and mark it paid when the money lands.",
};
const BOOKING_ACTIONS = [
  { key: "Confirmed", label: "Confirm" }, { key: "Done", label: "Mark done" }, { key: "Cancelled", label: "Cancel booking" },
];
const ENQUIRY_ACTIONS = ["Talking", "Quoted", "Won", "Lost"];

async function call<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, { credentials: "same-origin", ...init,
    headers: init?.body ? { "Content-Type": "application/json" } : undefined });
  if (res.status === 401) throw Object.assign(new Error("signed out"), { status: 401 });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw Object.assign(new Error(body.error ?? `Something went wrong (${res.status}).`), { status: res.status });
  }
  return res.status === 204 ? (undefined as T) : res.json();
}

export default function Admin() {
  const [signedIn, setSignedIn] = useState<boolean | null>(null);
  useEffect(() => { call("/api/admin/me").then(() => setSignedIn(true)).catch(() => setSignedIn(false)); }, []);
  if (signedIn === null) return <Shell help="Loading…"><p className="lead blink">Loading…</p></Shell>;
  return signedIn ? <Dashboard onSignedOut={() => setSignedIn(false)} /> : <SignIn onDone={() => setSignedIn(true)} />;
}

function Shell({ children, side, help }: { children: React.ReactNode; side?: React.ReactNode; help: string }) {
  return (
    <div className="stage">
      <div className="screen">
        <main className="win main-win">{children}</main>
        <aside className="side">{side}</aside>
        <footer className="win help">{help}</footer>
      </div>
      <p className="brand">BYTE CLUB · party leader menu</p>
    </div>
  );
}

function SignIn({ onDone }: { onDone: () => void }) {
  const [sent, setSent] = useState(false);
  const [code, setCode] = useState("");
  const [error, setError] = useState("");

  const send = async () => {
    setError("");
    try { await call("/api/admin/code", { method: "POST" }); setSent(true); }
    catch (e) { sfx.error(); setError((e as Error).message); }
  };
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    try { await call("/api/admin/session", { method: "POST", body: JSON.stringify({ code }) }); sfx.victory(); onDone(); }
    catch (err) { sfx.error(); setError((err as Error).message); setCode(""); }
  };

  return (
    <Shell help="Only the party leader can open this menu.">
      <section className="pop">
        <h1 className="win-title">Party leader sign-in</h1>
        {!sent ? (
          <>
            <p className="lead">A one-time code goes to your phone through ntfy.</p>
            <Menu label="Sign in" onSelect={() => void send()} items={[{ key: "send", label: "Send code to my phone" }]} />
          </>
        ) : (
          <form className="form" onSubmit={submit} noValidate>
            <label className="field" htmlFor="code">
              <span className="label">6-digit code from ntfy</span>
              <input id="code" value={code} autoFocus inputMode="numeric" autoComplete="one-time-code" maxLength={6}
                     onChange={(e) => setCode(e.target.value.replace(/\D/g, ""))} placeholder="123456" />
            </label>
            <button type="submit" className="cta"><Glove className="glove-inline" /> Enter</button>
            <button type="button" className="cta ghost" onClick={() => { setSent(false); setError(""); }}>Send a new code</button>
          </form>
        )}
        {error && <p className="msg" role="alert">{error}</p>}
      </section>
    </Shell>
  );
}

function Dashboard({ onSignedOut }: { onSignedOut: () => void }) {
  const [view, setView] = useState<View>("quests");
  const [bookings, setBookings] = useState<Booking[] | null>(null);
  const [enquiries, setEnquiries] = useState<Enquiry[] | null>(null);
  const [customers, setCustomers] = useState<Customer[] | null>(null);
  const [invoices, setInvoices] = useState<Invoice[] | null>(null);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [rate, setRate] = useState(80);
  const [open, setOpen] = useState<string | null>(null);
  const [message, setMessage] = useState("");

  const guard = (e: unknown) => {
    if ((e as { status?: number }).status === 401) onSignedOut();
    else { sfx.error(); setMessage((e as Error).message); }
  };
  const load = () => {
    call<Booking[]>("/api/admin/bookings").then(setBookings).catch(guard);
    call<Enquiry[]>("/api/admin/enquiries").then(setEnquiries).catch(guard);
    call<Customer[]>("/api/admin/customers").then(setCustomers).catch(guard);
    call<Invoice[]>("/api/admin/invoices").then(setInvoices).catch(guard);
    call<{ rate: number }>("/api/admin/invoices/defaults").then((d) => setRate(d.rate)).catch(() => {});
  };

  const startInvoice = (b: Booking) => {
    setDraft({
      bookingId: b.id, billToName: b.name, billToPhone: b.phone, billToEmail: b.email ?? "",
      lines: [{ description: `${b.service} - ${fmtDay(b.start)}`, hours: "1", rate: String(rate) }],
    });
    setView("items"); setOpen(null); setMessage("");
  };

  const saveInvoice = async () => {
    if (!draft) return;
    setMessage("");
    try {
      const made = await call<{ id: string }>("/api/admin/invoices", { method: "POST", body: JSON.stringify({
        bookingId: draft.bookingId, billToName: draft.billToName, billToPhone: draft.billToPhone,
        billToEmail: draft.billToEmail || undefined,
        lines: draft.lines.map((l) => ({ description: l.description, hours: Number(l.hours), rate: Number(l.rate) })),
      }) });
      sfx.victory(); setDraft(null); load(); setOpen(made.id);
    } catch (e) { guard(e); }
  };
  useEffect(load, []); // eslint-disable-line

  const setStatus = async (kind: "bookings" | "enquiries" | "invoices", id: string, status: string) => {
    setMessage("");
    try { await call(`/api/admin/${kind}/${id}`, { method: "PATCH", body: JSON.stringify({ status }) }); load(); }
    catch (e) { guard(e); }
  };
  const signOut = async () => { await call("/api/admin/logout", { method: "POST" }).catch(() => {}); onSignedOut(); };

  const live = bookings?.filter((b) => b.status === "Requested" || b.status === "Confirmed").length ?? 0;
  const fresh = enquiries?.filter((e) => e.status === "New").length ?? 0;

  const side = (
    <>
      <div className="win steps">
        <Menu label="Party leader menu" initial={view} autoFocus={false}
              onSelect={(k) => { if (k === "signout") void signOut(); else { setView(k as View); setOpen(null); setDraft(null); setMessage(""); } }}
              items={[
                { key: "quests", label: "Quest log", aside: live ? String(live) : undefined },
                { key: "ideas", label: "Ideas", aside: fresh ? String(fresh) : undefined },
                { key: "bestiary", label: "Bestiary" },
                { key: "items", label: "Items", aside: invoices?.some((i) => i.status === "Unpaid") ? String(invoices.filter((i) => i.status === "Unpaid").length) : undefined },
                { key: "signout", label: "Sign out" },
              ]} />
      </div>
      <div className="win clock">
        <div><span className="label">Live quests</span><span>{live}</span></div>
        <div><span className="label">New ideas</span><span>{fresh}</span></div>
      </div>
    </>
  );

  return (
    <Shell side={side} help={VIEW_HELP[view]}>
      <section className="pop" key={view + (open ?? "")}>
        {view === "quests" && (
          !bookings ? <p className="lead blink">Loading…</p> :
          open ? !bookings.some((b) => b.id === open) ? <p className="lead blink">Loading…</p> :
                 <BookingDetail b={bookings.find((b) => b.id === open)!} onBack={() => setOpen(null)}
                                onStatus={(s) => void setStatus("bookings", open, s)}
                                onInvoice={() => startInvoice(bookings.find((b) => b.id === open)!)} /> :
          <>
            <h1 className="win-title">Quest log</h1>
            {bookings.length === 0 ? <p className="lead">No bookings yet. They'll appear here and on your phone.</p> : (
              <Menu label="Bookings" onSelect={setOpen}
                    items={bookings.map((b) => ({
                      key: b.id, group: groupFor(b.status),
                      label: `${fmtDay(b.start)} ${fmtTime(b.start)} · ${b.service}`,
                      aside: `${b.name} · ${b.suburb}`,
                    }))} />
            )}
          </>
        )}

        {view === "ideas" && (
          !enquiries ? <p className="lead blink">Loading…</p> :
          open ? !enquiries.some((e) => e.id === open) ? <p className="lead blink">Loading…</p> :
                 <EnquiryDetail e={enquiries.find((e) => e.id === open)!} onBack={() => setOpen(null)}
                                onStatus={(s) => void setStatus("enquiries", open, s)} /> :
          <>
            <h1 className="win-title">Ideas</h1>
            {enquiries.length === 0 ? <p className="lead">No game or app ideas yet.</p> : (
              <Menu label="Ideas" onSelect={setOpen}
                    items={enquiries.map((e) => ({
                      key: e.id, group: e.status === "Won" || e.status === "Lost" ? "Closed" : "Open",
                      label: `${e.service} · ${e.name}`, aside: e.status,
                    }))} />
            )}
          </>
        )}

        {view === "bestiary" && (
          !customers ? <p className="lead blink">Loading…</p> :
          open ? !customers.some((c) => c.phone === open) ? <p className="lead blink">Loading…</p> :
                 <CustomerDetail c={customers.find((c) => c.phone === open)!} onBack={() => setOpen(null)} /> :
          <>
            <h1 className="win-title">Bestiary</h1>
            {customers.length === 0 ? <p className="lead">No customers yet.</p> : (
              <Menu label="Customers" onSelect={setOpen}
                    items={customers.map((c) => ({
                      key: c.phone, label: c.name,
                      aside: `${c.suburb ?? "Idea only"} · ${c.visits} visit${c.visits === 1 ? "" : "s"} done`,
                    }))} />
            )}
          </>
        )}
        {view === "items" && (
          draft ? <InvoiceForm draft={draft} onChange={setDraft} onSave={() => void saveInvoice()} onCancel={() => setDraft(null)} /> :
          !invoices ? <p className="lead blink">Loading…</p> :
          open ? !invoices.some((i) => i.id === open) ? <p className="lead blink">Loading…</p> :
                 <InvoiceDetail inv={invoices.find((i) => i.id === open)!} onBack={() => setOpen(null)}
                                onStatus={(s) => void setStatus("invoices", open, s)} /> :
          <>
            <h1 className="win-title">Items</h1>
            {invoices.length === 0 ? <p className="lead">No invoices yet. Open a booking in the quest log and choose Make invoice.</p> : null}
            <Menu label="Invoices" onSelect={(k) => k === "new" ? setDraft({ billToName: "", billToPhone: "", billToEmail: "", lines: [{ description: "", hours: "1", rate: String(rate) }] }) : setOpen(k)}
                  items={[{ key: "new", label: "New invoice" }, ...invoices.map((i) => ({
                    key: i.id, group: i.status === "Unpaid" ? "Waiting for payment" : i.status === "Paid" ? "Paid" : "Void",
                    label: `${i.number} · ${i.billToName}`, aside: `${money(i.total)} · due ${fmtDay(i.dueDate)}`,
                  }))]} />
          </>
        )}
        {message && <p className="msg" role="alert">{message}</p>}
      </section>
    </Shell>
  );
}

const groupFor = (s: string) => (s === "Requested" ? "Needs confirming" : s === "Confirmed" ? "Confirmed" : "Done and cancelled");

function BookingDetail({ b, onBack, onStatus, onInvoice }: { b: Booking; onBack: () => void; onStatus: (s: string) => void; onInvoice: () => void }) {
  return (
    <>
      <h1 className="win-title">{b.service}</h1>
      <dl className="summary">
        <dt>When</dt><dd>{fmtDay(b.start)}, {fmtTime(b.start)}</dd>
        <dt>Who</dt><dd>{b.name}</dd>
        <dt>Where</dt><dd>{b.address}, {b.suburb}</dd>
        <dt>Phone</dt><dd>{b.phone}</dd>
        {b.email && (<><dt>Email</dt><dd>{b.email}</dd></>)}
        {b.problem && (<><dt>Notes</dt><dd>{b.problem}</dd></>)}
        <dt>Status</dt><dd>{b.status}</dd>
      </dl>
      <Menu label="Booking actions" onBack={onBack}
            onSelect={(k) => k === "wa" ? window.open(b.whatsApp, "_blank") : k === "back" ? onBack() : k === "invoice" ? onInvoice() : onStatus(k)}
            items={[{ key: "wa", label: "WhatsApp them" },
                    { key: "invoice", label: "Make invoice" },
                    ...BOOKING_ACTIONS.filter((a) => a.key !== b.status),
                    { key: "back", label: "Back" }]} />
    </>
  );
}

function EnquiryDetail({ e, onBack, onStatus }: { e: Enquiry; onBack: () => void; onStatus: (s: string) => void }) {
  return (
    <>
      <h1 className="win-title">{e.service}</h1>
      <dl className="summary">
        <dt>Who</dt><dd>{e.name}</dd>
        <dt>Phone</dt><dd>{e.phone}</dd>
        {e.email && (<><dt>Email</dt><dd>{e.email}</dd></>)}
        <dt>Sent</dt><dd>{fmtDay(e.createdAt)}, {fmtTime(e.createdAt)}</dd>
        <dt>Idea</dt><dd>{e.idea}</dd>
        <dt>Status</dt><dd>{e.status}</dd>
      </dl>
      <Menu label="Idea actions" columns={2} onBack={onBack}
            onSelect={(k) => k === "wa" ? window.open(e.whatsApp, "_blank") : k === "back" ? onBack() : onStatus(k)}
            items={[{ key: "wa", label: "WhatsApp them" },
                    ...ENQUIRY_ACTIONS.filter((s) => s !== e.status).map((s) => ({ key: s, label: `Mark ${s.toLowerCase()}` })),
                    { key: "back", label: "Back" }]} />
    </>
  );
}

function CustomerDetail({ c, onBack }: { c: Customer; onBack: () => void }) {
  return (
    <>
      <h1 className="win-title">{c.name}</h1>
      <dl className="summary">
        <dt>Phone</dt><dd>{c.phone}</dd>
        {c.suburb && (<><dt>Suburb</dt><dd>{c.suburb}</dd></>)}
        <dt>Visits done</dt><dd>{c.visits}</dd>
      </dl>
      <div className="label">History</div>
      <ul className="history">
        {c.history.map((h, i) => (
          <li key={i}><span>{fmtDay(h.when)}</span> {h.what} <span className="label">{h.kind === "idea" ? "idea" : "visit"} · {h.status}</span></li>
        ))}
      </ul>
      <Menu label="Customer actions" onBack={onBack}
            onSelect={(k) => k === "wa" ? window.open(c.whatsApp, "_blank") : onBack()}
            items={[{ key: "wa", label: "WhatsApp them" }, { key: "back", label: "Back" }]} />
    </>
  );
}

function InvoiceForm({ draft, onChange, onSave, onCancel }: {
  draft: Draft; onChange: (d: Draft) => void; onSave: () => void; onCancel: () => void;
}) {
  const total = draft.lines.reduce((t, l) => t + Math.round(Number(l.hours || 0) * Number(l.rate || 0) * 100) / 100, 0);
  const line = (i: number, patch: Partial<Draft["lines"][number]>) =>
    onChange({ ...draft, lines: draft.lines.map((l, j) => (j === i ? { ...l, ...patch } : l)) });
  return (
    <>
      <h1 className="win-title">New invoice</h1>
      <form className="form" noValidate onSubmit={(e) => { e.preventDefault(); onSave(); }}
            onKeyDown={(e) => { if (e.key === "Escape") onCancel(); }}>
        <label className="field"><span className="label">Bill to</span>
          <input value={draft.billToName} onChange={(e) => onChange({ ...draft, billToName: e.target.value })} placeholder="Mary Jones" /></label>
        <label className="field"><span className="label">Phone</span>
          <input value={draft.billToPhone} onChange={(e) => onChange({ ...draft, billToPhone: e.target.value })} placeholder="0412 345 678" /></label>
        <label className="field"><span className="label">Email (optional)</span>
          <input value={draft.billToEmail} onChange={(e) => onChange({ ...draft, billToEmail: e.target.value })} placeholder="mary@example.com" /></label>
        <div className="lines">
          {draft.lines.map((l, i) => (
            <div className="line" key={i}>
              <input aria-label="Description" value={l.description} onChange={(e) => line(i, { description: e.target.value })} placeholder="What you did" />
              <input aria-label="Hours" inputMode="decimal" value={l.hours} onChange={(e) => line(i, { hours: e.target.value })} />
              <input aria-label="Rate" inputMode="decimal" value={l.rate} onChange={(e) => line(i, { rate: e.target.value })} />
              <span className="amount">{money(Math.round(Number(l.hours || 0) * Number(l.rate || 0) * 100) / 100)}</span>
              {draft.lines.length > 1 && <button type="button" className="x" aria-label="Remove line"
                onClick={() => onChange({ ...draft, lines: draft.lines.filter((_, j) => j !== i) })}>×</button>}
            </div>
          ))}
          <button type="button" className="cta ghost" onClick={() => onChange({ ...draft, lines: [...draft.lines, { description: "", hours: "1", rate: draft.lines[0]?.rate ?? "80" }] })}>Add line</button>
        </div>
        <p className="total">Total <strong>{money(total)}</strong></p>
        <button type="submit" className="cta"><Glove className="glove-inline" /> Save invoice</button>
        <button type="button" className="cta ghost" onClick={onCancel}>Cancel</button>
      </form>
    </>
  );
}

function InvoiceDetail({ inv, onBack, onStatus }: { inv: Invoice; onBack: () => void; onStatus: (s: string) => void }) {
  const pdf = `/api/admin/invoices/${inv.id}/pdf`;
  const first = inv.billToName.split(" ")[0];
  const wa = waLink(inv.billToPhone, `Hi ${first}, here's your Byte Club invoice ${inv.number} for ${money(inv.total)}. Thanks!`);
  return (
    <>
      <h1 className="win-title">{inv.number}</h1>
      <dl className="summary">
        <dt>Bill to</dt><dd>{inv.billToName}</dd>
        <dt>Total</dt><dd>{money(inv.total)}</dd>
        <dt>Issued</dt><dd>{fmtDay(inv.issueDate)}</dd>
        <dt>Due</dt><dd>{fmtDay(inv.dueDate)}</dd>
        <dt>Status</dt><dd>{inv.status}</dd>
      </dl>
      <p className="hint">Open the PDF, save it, then attach it in WhatsApp.</p>
      <Menu label="Invoice actions" onBack={onBack}
            onSelect={(k) => k === "pdf" ? window.open(pdf, "_blank") : k === "wa" ? window.open(wa, "_blank") : k === "back" ? onBack() : onStatus(k)}
            items={[{ key: "pdf", label: "Open PDF" }, { key: "wa", label: "WhatsApp them" },
                    ...(inv.status !== "Paid" ? [{ key: "Paid", label: "Mark paid" }] : [{ key: "Unpaid", label: "Mark unpaid" }]),
                    ...(inv.status !== "Void" ? [{ key: "Void", label: "Void invoice" }] : []),
                    { key: "back", label: "Back" }]} />
    </>
  );
}

function waLink(phone: string, text: string) {
  let digits = phone.replace(/\D/g, "");
  if (!phone.trim().startsWith("+") && digits.startsWith("0")) digits = "61" + digits.slice(1);
  return `https://wa.me/${digits}?text=${encodeURIComponent(text)}`;
}
