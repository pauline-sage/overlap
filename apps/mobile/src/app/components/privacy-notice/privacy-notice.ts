import { Component } from '@angular/core';
@Component({selector:'app-privacy-notice',template:`
<details>
  <summary>Data &amp; privacy</summary>
  <p>Your sign-in email and password are never shown to your partner. Passwords are stored as hashes. The hosted app uses HTTPS and protected session cookies; this is not end-to-end encryption.</p>
  <p>Your profile, presence and picks are private by default. Choose what you share in Settings; no partner approval is needed. You can stop sharing at any time. There is no partner view of your search history.</p>
  <p>Outing searches are personal. Use a city, address or one-time device location; search positions are not saved to your account or sent to your partner. Geoapify receives the location queries needed for search. Only an explicit shared venue or on-my-way message is saved for your partner; you can remove it at any time.</p>
  <p>Watch searches send search terms and country filters to TMDB and, when selected, YouTube. Posters load from their image services. Opening an external link takes you to that provider.</p>
  <p>Account data is processed by Overlap and its hosting/database services. The email provider handles account messages. When signup notifications are enabled, the site owner receives the new account's email address and signup time.</p>
  <p>Revocation stops further access; it cannot undo information already seen or saved by someone else. Hosting logs and backups have their own retention; a deleted account does not imply every historical copy has vanished.</p>
</details>`,styles:[`:host{display:block;margin:1.25rem 0}details{font-size:.8rem;line-height:1.6;color:var(--muted)}summary{cursor:pointer;min-height:44px;align-content:center;color:var(--text)}`]})
export class PrivacyNoticeComponent {}
