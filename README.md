# Civiti Web

Frontend-ul platformei [Civiti](https://civiti.ro) — aplicație web de participare civică pentru România.

[![Live](https://img.shields.io/badge/live-civiti.ro-blue)](https://civiti.ro)

## Ce face

Civiti le permite cetățenilor să raporteze probleme din comunitatea lor și să coordoneze campanii de email către autoritățile locale responsabile. Aplicația web oferă:

- Creare și vizualizare probleme civice cu localizare pe hartă
- Campanii coordonate de email către autorități
- Sistem de gamificare (puncte, badge-uri, realizări)
- Panou de administrare pentru moderarea conținutului
- Autentificare cu Google OAuth sau email/parolă

## Tech Stack

- **Framework**: Angular 19 (standalone components, signals, new control flow)
- **State Management**: NgRx (store, effects, selectors)
- **UI Library**: NG-ZORRO Ant Design
- **Styling**: Tailwind CSS + SCSS + CSS custom properties
- **Auth**: Supabase Auth
- **Rendering**: SSR pe Vercel
- **Locale**: Română (ro)

## Dezvoltare locală

```bash
npm install
npm run start:dev
```

Necesită un fișier `.env` — vezi `.env.example` pentru variabilele necesare.

## Deployment

Frontend-ul este deploy-at pe **Vercel** cu Server-Side Rendering. Backend-ul rulează separat pe Railway — vezi [civiti-server](https://github.com/civiti/civiti-server).

## Alte repo-uri

- [civiti-server](https://github.com/civiti/civiti-server) — Backend (.NET 8 / C#)
- [civiti-mobile](https://github.com/civiti/civiti-mobile) — Aplicație mobilă (Expo / React Native)
