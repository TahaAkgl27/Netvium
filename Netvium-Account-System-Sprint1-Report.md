# Netvium Account System — Sprint 1 Raporu

## 1. Mevcut mimari analizi

- Masaüstü uygulama .NET 8 WPF/WinForms hibriti; RDP ActiveX, Dashboard, Agent, Licensing ve AI Guide aynı `Netvium` projesinde.
- Sprint öncesinde hesap/authentication katmanı yoktu. Uygulama `StartupUri="MainWindow.xaml"` ile koşulsuz açılıyor, sağ üst profil `Environment.UserName` gösteriyordu.
- Çözümde `NetviumIdentity` için SQL şema betiği, EF modeli veya bağlantı ayarı bulunmadı. Canlı SQL erişimi/secreti kullanılmadı.
- Masaüstü projeye EF/SQL paketi eklenmedi; SQL erişimi yalnızca yeni ve ayrı Web API projesinde.
- RDP, Dashboard, Agent, mevcut lisans ve AI Guide iş akışlarına işlevsel değişiklik yapılmadı.

## 2. Eklenen/değiştirilen dosyalar

Ana değişiklik grupları:

- `Netvium.Identity.Api/`: ayrı ASP.NET Core API, EF Core SQL Server eşlemeleri, JWT/refresh güvenliği, auth servisleri, şema doğrulama ve güvenli configuration.
- `Netvium.Identity.Api.Tests/`: WebApplicationFactory tabanlı API entegrasyon testleri.
- `Models/Account/` ve `Services/Account/`: masaüstü domain modelleri, typed HTTP client, hesap servisi, DPAPI refresh token/değişmez cihaz kimliği saklama.
- `Views/Account/`: Netvium tasarımlı Login/Register penceresi.
- `Netvium.Account.Tests/`: masaüstü hesap servisi unit testleri.
- `App.xaml`, `App.xaml.cs`, `App.Account.cs`: zorunlu başlangıç oturum kontrolü ve login/main geçişi.
- `MainWindow.xaml`, `MainWindow.xaml.cs`: gerçek DisplayName/Email ve API + lokal temizleme yapan çıkış.
- `Models/AppSettings.cs`: tek masaüstü API base URL ayarı.
- `Netvium.sln`, `Netvium.csproj`: yeni projeler ve kaynak glob izolasyonu.

## 3. API endpointleri

| Method | Yol | Davranış |
|---|---|---|
| POST | `/api/auth/register` | Normalize email, parola politikası, duplicate kontrolü, transaction içinde kullanıcı + aktif FREE lisans |
| POST | `/api/auth/login` | Generic credential hatası, 5 hatada 15 dk lockout, cihaz upsert, JWT + refresh |
| POST | `/api/auth/refresh` | SHA-256 hash lookup, rotation, eski token reuse halinde tüm family iptali |
| POST | `/api/auth/logout` | Refresh token iptali ve LOGOUT audit |
| GET | `/api/account/me` | Bearer JWT; yalnızca izin verilen hesap/lisans alanları |
| GET | `/health` | Secret göstermeden configuration, DB bağlantısı ve `dbo` şema sözleşmesi durumu |

Register/login/refresh için doğrudan bağlantı IP’sine göre ayrı fixed-window rate limitleri vardır. JWT issuer, audience, signature, expiration ve 30 saniyelik clock skew ile doğrulanır.

## 4. Güvenlik kararları

- Parolalar `PasswordHasher<User>` ile hashlenir; plaintext parola saklanmaz/loglanmaz.
- Access token yalnızca masaüstü proses belleğinde tutulur.
- Refresh token API yanıtında bir kez üretilir; veritabanında yalnızca 64 karakter SHA-256 hex hash saklanır.
- Refresh token masaüstünde CurrentUser DPAPI ile `%LocalAppData%/Netvium/Identity/refresh.dat` içinde korunur.
- Refresh rotation ve reuse-family revocation uygulanmıştır.
- Bilinmeyen kullanıcı, yanlış parola, pasif kullanıcı ve lockout aynı `AUTH_INVALID_CREDENTIALS` yanıtını kullanır; login enumeration yapılmaz.
- Audit eventleri: `REGISTER_SUCCESS`, `REGISTER_FAILED`, `LOGIN_SUCCESS`, `LOGIN_FAILED`, `TOKEN_REFRESH`, `LOGOUT`.
- API request body/token/parola loglamaz. EF sensitive-data logging açılmaz. Masaüstü transport exception ayrıntısını kullanıcıya/loga taşımaz.
- API migration çalıştırmaz, tablo oluşturmaz ve production SQL üzerinde test verisi üretmez.
- Şema uyuşmazsa auth endpointleri `SCHEMA_CONTRACT_MISMATCH` ile güvenli biçimde 503 döner.
- HTTP base URL yalnızca loopback geliştirme için kabul edilir; uzak ortamlar HTTPS kullanmak zorundadır.

## 5. Configuration anahtarları

API — User Secrets veya environment/secret store:

- `ConnectionStrings:NetviumIdentity`
- `Jwt:Issuer`
- `Jwt:Audience`
- `Jwt:SigningKey` — en az 32 byte rastgele değer

`appsettings.json` içinde bu değerler boş bırakılmıştır. Token süreleri kod tarafından Sprint 1 sözleşmesine sabitlenir: access 15 dakika, refresh 30 gün.

Masaüstü:

- `AppSettings.IdentityApiBaseUrl` — varsayılan yerel geliştirme adresi `http://localhost:5187/`.

## 6. Build ve test sonuçları

- Tam çözüm build: başarılı (`dotnet build Netvium.sln --configuration Debug --no-restore -m:1 -p:UseAppHost=false ...`). Sonuç: 0 error, 8 warning.
- Warningler mevcut `RdpSessionTabView` içindeki dört kullanılmayan event için `CS0067` uyarısıdır. WPF geçici + ana derleme aşaması nedeniyle iki kez raporlanır; Sprint 1 hesap kodundan yeni warning yok.
- API entegrasyon testleri: **11/11 geçti**. İstenen kayıt, duplicate, zayıf parola, login, yanlış parola, lockout, pasif kullanıcı, refresh/rotation/reuse, logout, yetkisiz `/me`, FREE lisans ve raw refresh saklanmaması senaryolarını kapsar.
- Desktop unit testleri: **6/6 geçti**. Login state, failure state, refresh, logout lokal temizleme, API kesintisi ve secret sızıntısız hata senaryolarını kapsar.
- Mevcut AI Guide testleri: **94 geçti, 2 ortam nedeniyle başarısız**. Eski offline lisans testleri FAZ 6E kapsamında aktif test derlemesinden çıkarıldı; gerçek `%LocalAppData%/Netvium/license.dat` dosyasına dokunulmadı.
- Secret pattern taraması: yeni source/config üzerinde gerçek SQL connection string, `sa`, doldurulmuş signing key veya gerçek token bulunmadı.
- API boş configuration ile manuel başlatıldığında `/health`: HTTP 503 ve yalnızca `IDENTITY_NOT_CONFIGURED` döndürdü.

## 7. Manuel çalıştırma adımları

1. Yerel secrets değerlerini kaynak dosyaya yazmadan ayarlayın:

   ```powershell
   dotnet user-secrets --project .\Netvium.Identity.Api set "ConnectionStrings:NetviumIdentity" "<local-development-value>"
   dotnet user-secrets --project .\Netvium.Identity.Api set "Jwt:Issuer" "<issuer>"
   dotnet user-secrets --project .\Netvium.Identity.Api set "Jwt:Audience" "<audience>"
   dotnet user-secrets --project .\Netvium.Identity.Api set "Jwt:SigningKey" "<minimum-32-byte-random-value>"
   ```

2. API’yi çalıştırın: `dotnet run --project .\Netvium.Identity.Api`.
3. `GET http://localhost:5187/health` sonucunun `healthy` ve `schema=valid` olduğunu doğrulayın.
4. Masaüstünü çalıştırın. Oturum yoksa Login/Register ekranı açılır; kayıt sonrası login yapın.
5. Ana ekranda DisplayName/Email, uygulama yeniden açılışında session refresh ve sağ üst çıkış akışını kontrol edin.

## 8. Eksik veya bloke kalan noktalar

- Repository’de gerçek `NetviumIdentity` DDL’i ve güvenli çalışma zamanı bağlantısı bulunmadığı için canlı kolon adları bu ortamda sorgulanamadı. Kod, Sprint sözleşmesinden türetilen beklenen kolonları `INFORMATION_SCHEMA.COLUMNS` ile canlı ortamda doğrular ve uyuşmazlıkta auth’u bloklar.
- Live SQL doğrulaması tamamlanmadan production deploy yapılmamalı. `/health` tarafından hata alınırsa API’nin beklediği kolon sözleşmesi mevcut DDL ile karşılaştırılmalı; tablo/migration otomatik üretilmemelidir.
- Mevcut iki lisans testi kullanıcı LocalAppData’sına yazdığı için sandbox içinde geçemedi; test storage izolasyonu Premium/lisans kapsamının sahibi tarafından ayrıca ele alınmalı.

## 9. Premium Sprint 2 için dokunulmadan bırakılan entegrasyon noktaları

- Aktif entitlement `FeatureEntitlementService` üzerinden hesap/plan kaynağından gelir; dormant offline lisans yüzeyi ve `license.dat` üretim derlemesinden çıkarılmıştır. Plans UI davranışı korunmuştur.
- FREE hesap lisansı yalnızca identity DB hesap profilinin parçasıdır; Premium feature entitlement’a bağlanmadı.
- Ödeme, abonelik, Premium/Premium+ aktivasyonu, setup ve otomatik güncelleme işlerine girilmedi.
- Sprint 2’de identity `planCode/licenseStatus` ile mevcut entitlement katmanı arasında açık bir server-authoritative adaptör tasarlanabilir; Sprint 1’de bypass veya sahte Premium state eklenmedi.
