import { useEffect, useMemo, useState } from "react";
import { api, ApiError, dayKey, fmtDay, fmtTime, type BookingResult, type Service, type Slot } from "./api";
import { Glove } from "./Glove";
import { Menu } from "./Menu";
import { setSound, sfx, soundOn } from "./sound";

type Screen = "title" | "quest" | "time" | "party" | "save" | "victory";
const STEPS: { key: Screen; label: string }[] = [
  { key: "quest", label: "Quest" }, { key: "time", label: "Time" }, { key: "party", label: "Party" }, { key: "save", label: "Save" },
];
const HELP: Record<Screen, string> = {
  title: "Gold Coast · home visits · computers, phones, Wi-Fi",
  quest: "What needs fixing? Choose a quest.",
  time: "Pick a day and a time for Nick to visit.",
  party: "Your details, so Nick can find you and confirm.",
  save: "Check everything, then save your booking.",
  victory: "Booking saved. Nick will confirm on WhatsApp.",
};
const WHATSAPP = "https://wa.me/64211902813";

type Party = { name: string; phone: string; email: string; suburb: string; address: string; problem: string };
const EMPTY: Party = { name: "", phone: "", email: "", suburb: "", address: "", problem: "" };

export default function App() {
  const [screen, setScreen] = useState<Screen>("title");
  const [services, setServices] = useState<Service[]>([]);
  const [slots, setSlots] = useState<Slot[] | null>(null);
  const [quest, setQuest] = useState<Service | null>(null);
  const [peek, setPeek] = useState<Service | null>(null);
  const [day, setDay] = useState<string | null>(null);
  const [slot, setSlot] = useState<Slot | null>(null);
  const [party, setParty] = useState<Party>(EMPTY);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<BookingResult | null>(null);
  const [sound, setSoundState] = useState(soundOn());
  const [now, setNow] = useState(new Date());

  useEffect(() => { api.services().then(setServices).catch(() => setMessage("Couldn't load quests. Refresh, or WhatsApp Nick.")); }, []);
  useEffect(() => { const t = setInterval(() => setNow(new Date()), 15000); return () => clearInterval(t); }, []);

  const go = (s: Screen) => { setMessage(""); setScreen(s); };
  const shown = peek ?? quest;

  const loadSlots = () => {
    setSlots(null);
    api.slots().then((s) => { setSlots(s); setDay((d) => d ?? (s[0] ? dayKey(s[0].start) : null)); })
      .catch(() => setMessage("Couldn't load times. Refresh, or WhatsApp Nick."));
  };

  const days = useMemo(() => {
    const m = new Map<string, Slot[]>();
    for (const s of slots ?? []) m.set(dayKey(s.start), [...(m.get(dayKey(s.start)) ?? []), s]);
    return m;
  }, [slots]);

  const toggleSound = () => { const on = !sound; setSound(on); setSoundState(on); if (on) sfx.confirm(); };

  const submit = async () => {
    if (!quest || !slot) return;
    setBusy(true); setMessage("");
    try {
      const r = await api.book({ serviceId: quest.id, start: slot.start, ...party, email: party.email || undefined, problem: party.problem || undefined });
      setResult(r); sfx.victory(); go("victory");
    } catch (e) {
      sfx.error();
      const err = e as ApiError;
      if (err.status === 409) { setSlot(null); loadSlots(); setScreen("time"); setMessage(err.message); }
      else if (err.status === 400) { setErrors(err.fields); setScreen("party"); setMessage(err.message); }
      else setMessage(err.message);
    } finally { setBusy(false); }
  };

  const checkParty = () => {
    const e: Record<string, string> = {};
    if (!party.name.trim()) e.name = "Enter your name.";
    if (party.phone.replace(/\D/g, "").length < 8) e.phone = "Enter a phone number Nick can WhatsApp.";
    if (!party.suburb.trim()) e.suburb = "Enter your suburb.";
    if (!party.address.trim()) e.address = "Enter the address for the visit.";
    if (party.email && !party.email.includes("@")) e.email = "That email doesn't look right.";
    setErrors(e);
    if (Object.keys(e).length) { sfx.error(); return; }
    sfx.confirm(); go("save");
  };

  const focusMenu = (label: string) =>
    setTimeout(() => document.querySelector<HTMLButtonElement>(`[aria-label="${label}"] [tabindex="0"]`)?.focus(), 0);

  const reset = () => { setQuest(null); setPeek(null); setSlot(null); setParty(EMPTY); setResult(null); setSlots(null); setDay(null); go("title"); };

  return (
    <div className="stage">
      <div className="screen">
        <main className="win main-win" key={screen}>
          {screen === "title" && (
            <section className="pop">
              <PartyRow name="Nick" role="Tech wizard" lv={99} hp={[9999, 9999]} mp={[999, 999]} portrait="N" />
              <PartyRow name="Your device" role="Status: ???" hp={[12, 100]} portrait="?" hurt />
              <p className="lead">A wild broken laptop appeared. Book a home visit and Nick joins your party.</p>
              <Menu label="Main menu" initial="book" onSelect={(k) => k === "book" ? go("quest") : window.open(WHATSAPP, "_blank")}
                    items={[{ key: "book", label: "Book a visit" }, { key: "whatsapp", label: "WhatsApp Nick" }]} />
            </section>
          )}

          {screen === "quest" && (
            <section className="pop">
              <h1 className="win-title">Choose your quest</h1>
              <Menu label="Choose your quest" initial={quest?.id}
                    onFocusChange={(k) => setPeek(services.find((s) => s.id === k) ?? null)}
                    onBack={() => go("title")}
                    onSelect={(k) => { setQuest(services.find((s) => s.id === k)!); if (!slots) loadSlots(); go("time"); }}
                    items={services.map((s) => ({ key: s.id, label: s.name, aside: s.blurb }))} />
              {shown && (
                <div className="sub-win">
                  <PartyRow name={`Your ${shown.device.toLowerCase()}`} role={`Status: ${shown.status}`} hp={[shown.deviceHp, 100]} portrait="?" hurt />
                </div>
              )}
            </section>
          )}

          {screen === "time" && (
            <section className="pop">
              <h1 className="win-title">Time gate</h1>
              {slots === null ? <p className="lead blink">Opening the time gate…</p> : days.size === 0 ? (
                <p className="lead">No free times in the next two weeks. <a href={WHATSAPP}>WhatsApp Nick</a> and he'll fit you in.</p>
              ) : (
                <div className="time-grid">
                  <div>
                    <div className="label">Day</div>
                    <Menu label="Choose a day" initial={day ?? undefined} onBack={() => go("quest")}
                          onFocusChange={(k) => setDay(k)} onSelect={(k) => { setDay(k); focusMenu("Choose a time"); }}
                          onRight={() => focusMenu("Choose a time")}
                          items={[...days.keys()].map((k) => ({ key: k, label: fmtDay(days.get(k)![0].start), aside: `${days.get(k)!.length} free` }))} />
                  </div>
                  <div>
                    <div className="label">Time</div>
                    {day && days.get(day) && (
                      <Menu label="Choose a time" key={day} columns={2} initial={slot?.start} autoFocus={false}
                            onBack={() => focusMenu("Choose a day")}
                            onSelect={(k) => { setSlot(days.get(day)!.find((s) => s.start === k)!); go("party"); }}
                            items={days.get(day)!.map((s) => ({ key: s.start, label: fmtTime(s.start) }))} />
                    )}
                  </div>
                </div>
              )}
            </section>
          )}

          {screen === "party" && (
            <section className="pop">
              <h1 className="win-title">Party details</h1>
              <form className="form" noValidate onSubmit={(e) => { e.preventDefault(); checkParty(); }}
                    onKeyDown={(e) => { if (e.key === "Escape") { sfx.back(); go("time"); } }}>
                <Field id="name" label="Name" autoFocus value={party.name} error={errors.name} autoComplete="name" onChange={(v) => setParty({ ...party, name: v })} placeholder="Mary Jones" />
                <Field id="phone" label="Phone (WhatsApp)" value={party.phone} error={errors.phone} autoComplete="tel" inputMode="tel" onChange={(v) => setParty({ ...party, phone: v })} placeholder="0412 345 678" />
                <Field id="suburb" label="Suburb" value={party.suburb} error={errors.suburb} autoComplete="address-level2" onChange={(v) => setParty({ ...party, suburb: v })} placeholder="Southport" />
                <Field id="address" label="Address for the visit" value={party.address} error={errors.address} autoComplete="street-address" onChange={(v) => setParty({ ...party, address: v })} placeholder="12 Example St" />
                <Field id="email" label="Email (optional)" value={party.email} error={errors.email} autoComplete="email" inputMode="email" onChange={(v) => setParty({ ...party, email: v })} placeholder="mary@example.com" />
                <Field id="problem" label="What's going on? (optional)" value={party.problem} error={errors.problem} multiline onChange={(v) => setParty({ ...party, problem: v })} placeholder="It's really slow and shows pop-ups" />
                <button type="submit" className="cta"><Glove className="glove-inline" /> Continue</button>
              </form>
            </section>
          )}

          {screen === "save" && quest && slot && (
            <section className="pop">
              <h1 className="win-title">Save booking?</h1>
              <dl className="summary">
                <dt>Quest</dt><dd>{quest.name}</dd>
                <dt>When</dt><dd>{fmtDay(slot.start)}, {fmtTime(slot.start)}–{fmtTime(slot.end)}</dd>
                <dt>Where</dt><dd>{party.address}, {party.suburb}</dd>
                <dt>Name</dt><dd>{party.name}</dd>
                <dt>Phone</dt><dd>{party.phone}</dd>
                {party.problem && (<><dt>Notes</dt><dd>{party.problem}</dd></>)}
              </dl>
              {busy ? <p className="lead blink">Saving…</p> : (
                <Menu label="Save booking" initial="yes" columns={2} onBack={() => go("party")}
                      onSelect={(k) => k === "yes" ? void submit() : go("party")}
                      items={[{ key: "yes", label: "Yes, book it" }, { key: "no", label: "No, go back" }]} />
              )}
            </section>
          )}

          {screen === "victory" && result && slot && (
            <section className="pop victory">
              <h1 className="fanfare">Quest accepted!</h1>
              <p className="lead">Nick joined {result.firstName}'s party.</p>
              <dl className="summary">
                <dt>Quest</dt><dd>{result.service}</dd>
                <dt>When</dt><dd>{fmtDay(slot.start)}, {fmtTime(slot.start)}</dd>
                <dt>Next</dt><dd>Nick will WhatsApp you to confirm.</dd>
              </dl>
              <p className="spoils">Received: 1 booking · EXP +100</p>
              <Menu label="Done" onSelect={reset} items={[{ key: "done", label: "Back to menu" }]} />
            </section>
          )}

          {message && <p className="msg" role="alert">{message}</p>}
        </main>

        <aside className="side">
          <nav className="win steps" aria-label="Booking steps">
            {STEPS.map((s) => (
              <div key={s.key} className={`step${screen === s.key ? " is-on" : ""}`} aria-current={screen === s.key ? "step" : undefined}>
                {screen === s.key && <Glove className="glove" />}{s.label}
              </div>
            ))}
          </nav>
          <div className="win clock">
            <div><span className="label">Time</span><span>{now.toLocaleTimeString("en-AU", { hour: "2-digit", minute: "2-digit", hour12: false, timeZone: "Australia/Brisbane" })}</span></div>
            <div><span className="label">Gil</span><span>Quote first</span></div>
          </div>
          <button type="button" className="win sound" onClick={toggleSound} aria-pressed={sound}>
            Sound {sound ? "on" : "off"}
          </button>
        </aside>

        <footer className="win help">{HELP[screen]}</footer>
      </div>
      <p className="brand">BYTE CLUB · Gold Coast tech help · <a href={WHATSAPP}>WhatsApp +64 21 190 2813</a></p>
    </div>
  );
}

function PartyRow({ name, role, lv, hp, mp, portrait, hurt }: {
  name: string; role: string; lv?: number; hp: [number, number]; mp?: [number, number]; portrait: string; hurt?: boolean;
}) {
  const pct = Math.max(2, Math.round((hp[0] / hp[1]) * 100));
  return (
    <div className="party-row">
      <div className={`portrait${hurt ? " hurt" : ""}`}>{portrait}</div>
      <div className="stats">
        <div className="name">{name} <span className="label">{role}</span></div>
        {lv !== undefined && <div><span className="label">LV</span> {lv}</div>}
        <div className="bar-row"><span className="label">HP</span> <span>{hp[0]}/{hp[1]}</span>
          <div className="bar"><i style={{ width: `${pct}%` }} className={pct < 30 ? "low" : ""} /></div></div>
        {mp && <div className="bar-row"><span className="label">MP</span> <span>{mp[0]}/{mp[1]}</span>
          <div className="bar"><i className="mp" style={{ width: "100%" }} /></div></div>}
      </div>
    </div>
  );
}

function Field({ id, label, value, onChange, error, multiline, ...rest }: {
  id: string; label: string; value: string; onChange: (v: string) => void; error?: string; multiline?: boolean;
} & Omit<React.InputHTMLAttributes<HTMLInputElement>, "onChange" | "value" | "id">) {
  return (
    <label className={`field${error ? " has-error" : ""}`} htmlFor={id}>
      <span className="label">{label}</span>
      {multiline
        ? <textarea id={id} value={value} rows={2} maxLength={1000} placeholder={rest.placeholder} onChange={(e) => onChange(e.target.value)} />
        : <input id={id} value={value} onChange={(e) => onChange(e.target.value)} aria-invalid={!!error} {...rest} />}
      {error && <span className="field-error">{error}</span>}
    </label>
  );
}
