export type Service = {
  id: string; name: string; blurb: string; device: string; status: string; deviceHp: number;
  kind: "visit" | "project"; isProject: boolean;
};
export type Slot = { start: string; end: string };
export type BookingRequest = {
  serviceId: string; start: string; name: string; phone: string; email?: string;
  suburb?: string; address?: string; problem?: string;
};
export type BookingResult = { id: string; service: string; start: string; firstName: string };
export type EnquiryRequest = { serviceId: string; name: string; phone: string; email?: string; idea: string };
export type EnquiryResult = { id: string; service: string; firstName: string; start?: undefined };

export class ApiError extends Error {
  fields: Record<string, string>;
  status: number;
  constructor(message: string, fields: Record<string, string> = {}, status = 0) {
    super(message);
    this.fields = fields;
    this.status = status;
  }
}

async function json<T>(res: Response): Promise<T> {
  if (res.ok) return res.json() as Promise<T>;
  const body = await res.json().catch(() => ({}));
  if (res.status === 400 && body.errors) {
    const fields = Object.fromEntries(Object.entries(body.errors as Record<string, string[]>).map(([k, v]) => [k, v[0]]));
    throw new ApiError("Check the highlighted fields.", fields, 400);
  }
  if (res.status === 429) throw new ApiError("Too many tries. Wait a few minutes, or WhatsApp Nick.", {}, 429);
  throw new ApiError(body.error ?? "Something went wrong. Try again, or WhatsApp Nick.", {}, res.status);
}

// The API sleeps when nobody's booking. While it wakes, the proxy in front of it times out (502-504)
// or the request fails outright, so reads keep trying for up to three minutes before giving up.
async function getWhileWaking<T>(url: string): Promise<T> {
  const giveUp = Date.now() + 180_000;
  for (;;) {
    try {
      const res = await fetch(url);
      if (res.status < 502 || res.status > 504 || Date.now() > giveUp) return json<T>(res);
    } catch (e) {
      if (Date.now() > giveUp) throw e;
    }
    await new Promise((r) => setTimeout(r, 4000));
  }
}

export const api = {
  services: () => getWhileWaking<Service[]>("/api/services"),
  slots: () => getWhileWaking<Slot[]>("/api/slots"),
  enquire: (e: EnquiryRequest) =>
    fetch("/api/enquiries", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(e) })
      .then((r) => json<EnquiryResult>(r)),
  book: (b: BookingRequest) =>
    fetch("/api/bookings", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(b) })
      .then((r) => json<BookingResult>(r)),
};

const TZ = "Australia/Brisbane";
export const fmtDay = (iso: string) =>
  new Date(iso).toLocaleDateString("en-AU", { weekday: "short", day: "numeric", month: "short", timeZone: TZ });
export const fmtTime = (iso: string) =>
  new Date(iso).toLocaleTimeString("en-AU", { hour: "numeric", minute: "2-digit", timeZone: TZ }).replace(" ", "").toLowerCase();
export const dayKey = (iso: string) =>
  new Date(iso).toLocaleDateString("en-CA", { timeZone: TZ });
