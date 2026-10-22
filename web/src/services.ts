import type { Service } from "./api";

// The quest list, built into the page so it shows instantly even while the server is asleep.
// Mirrors Services.All in api/Domain.cs; the page still asks the API and swaps in its list if it differs.
const visit = (id: string, name: string, blurb: string, device: string, status: string, deviceHp: number): Service =>
  ({ id, name, blurb, device, status, deviceHp, kind: "visit", isProject: false });
const project = (id: string, name: string, blurb: string, device: string, status: string, deviceHp: number): Service =>
  ({ id, name, blurb, device, status, deviceHp, kind: "project", isProject: true });

export const SERVICES: Service[] = [
  visit("laptop", "Laptop / PC repair", "Slow, won't start, broken screen, virus", "Laptop", "Slow, Virus?", 12),
  visit("phone", "Phone fix", "Cracked screen, battery, setup", "Phone", "Cracked", 40),
  visit("wifi", "Wi-Fi and printer", "No internet, printer won't print", "Router", "Offline", 0),
  visit("scam", "Scam cleanup", "Clicked a dodgy link, remote access, fake calls", "Computer", "Cursed", 5),
  visit("setup", "New device setup", "Transfer your stuff, email, backups", "New device", "Unequipped", 100),
  visit("other", "Something else", "Tell Nick what's going on", "Mystery device", "Unknown", 50),
  project("game", "Make a video game", "From idea to playable. Quoted after a chat", "Game idea", "Unbuilt", 100),
  project("app", "Build an app", "Apps, tools and AI. Quoted after a chat", "App idea", "Unbuilt", 100),
];
