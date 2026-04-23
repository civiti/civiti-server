# Civiti Server

Backend-ul platformei [Civiti](https://civiti.ro) — API REST pentru aplicația de participare civică.

## Ce face

API-ul gestionează toată logica de business a platformei Civiti:

- CRUD pentru probleme civice (issues)
- Campanii coordonate de email către autoritățile locale
- Moderare conținut via Google Perspective API
- Autentificare și autorizare cu Supabase Auth (JWT)
- Generare QR code și PDF-uri pentru partajarea problemelor
- Email-uri tranzacționale via Resend

## Tech Stack

- **Framework**: .NET 8 Minimal API, C#
- **Auth**: Supabase Auth (JWT validation, custom claims pentru roluri)
- **Database**: PostgreSQL (via Supabase)
- **Email**: Resend (template-uri în română)
- **Content Moderation**: Google Perspective API
- **Deployment**: Railway (dev + production)
- **Containerizare**: Docker

## Dezvoltare locală

```bash
dotnet restore
dotnet run --project Civiti.Api
```

Necesită un fișier `.env` — vezi `.env.example` pentru variabilele necesare.

## Deployment

Serverul este deploy-at pe **Railway** cu două medii: `dev` și `production`. Deploy-ul se face automat la push pe branch-ul corespunzător.

## Alte repo-uri

- [civiti-web](https://github.com/civiti/civiti-web) — Frontend (Angular 19)
- [civiti-mobile](https://github.com/civiti/civiti-mobile) — Aplicație mobilă (Expo / React Native)
