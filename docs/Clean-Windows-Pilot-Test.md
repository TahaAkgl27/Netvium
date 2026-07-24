# Netvium — Temiz Windows 11 Pilot Test Planı

Bu belge canlı kurulum doğrulama planıdır. Temiz bir Windows 11 x64 VM yoksa sonuçlar **beklemede** raporlanır; başarı uydurulmaz.

## Ön koşullar

- Temiz Windows 11 x64 VM ve geri dönülebilir snapshot.
- Yerel yönetici hesabı/UAC açık.
- Kontrollü pilot release klasörü, `release-manifest.json`, `SHA256SUMS.txt` ve installer.
- Framework-dependent pakette .NET 8 Desktop Runtime x64 kurulu olmalı; self-contained pakette bu ön koşul aranmaz.
- Gerçek SQL/RDP/AI endpoint bilgileri test paketine yazılmaz.

## Senaryo sırası

1. VM snapshot alın; başlangıçta `%LocalAppData%\Netvium` ve `%ProgramData%\Netvium` durumunu kaydedin.
2. .NET 8 Desktop Runtime yok senaryosunda framework-dependent installer çalıştırın. Anlaşılır prerequisite sonucu beklenir; sessiz crash kabul edilmez.
3. Runtime mevcut senaryosunda per-machine kurulumu `%ProgramFiles%\Netvium` altına yapın.
4. UAC istemi, Add/Remove Programs kaydı, Start Menu shortcut ve isteğe bağlı Desktop shortcut kontrol edin.
5. Uygulamayı ilk kez açın; başlangıç crash’i, eksik DLL/COM ve güvenli hata mesajlarını kontrol edin.
6. SmartScreen/Publisher sonucunu kaydedin. İmzasız pilot paket açıkça “UNSIGNED PILOT BUILD” olarak değerlendirilir.
7. `%LocalAppData%\Netvium` ve `%ProgramData%\Netvium` klasörlerinin kullanıcı verisi için korunduğunu doğrulayın.
8. Uygulama açıkken upgrade çalıştırın; kullanıcı bilgilendirmesi ve rollback davranışını kaydedin.
9. Daha eski sürüme rollback yapın; uygulama ve kullanıcı verilerinin durumu kontrol edin.
10. Agent feature seçilmeden kurulum yapın; Agent servisi kurulmamış olmalı.
11. Agent feature seçilirse dosyaların geldiğini, fakat secret command line’a yazılmadan DPAPI provisioning için manuel yönetici adımının gösterildiğini kontrol edin. Otomatik firewall/remote bind açılmamalı.
12. Agent service manuel provisioning sonrasında start/stop/recovery akışını `Install-NetviumAgentService.ps1` ve `Test-NetviumAgentService.ps1` ile doğrulayın.
13. Uninstall çalıştırın. Varsayılan uninstall `%LocalAppData%\Netvium` ve `%ProgramData%\Netvium` verilerini silmemeli; “Remove user data” seçilmedikçe veri korunmalı.
14. Logları toplayın; parola, token, API key, SQL connection string ve Agent secret bulunmadığını doğrulayın.

## Başarı kriterleri

- Kurulum/upgrade/rollback/uninstall hata vermeden tamamlanır.
- UAC ve shortcut davranışı beklenir.
- Runtime eksikliği anlaşılır şekilde raporlanır.
- Kullanıcı verisi varsayılan uninstall’da korunur.
- Agent varsayılan olarak loopback/local-only kalır; firewall kuralı eklenmez.
- Paket hash’leri gate tarafından doğrulanır.

## Başarısızlık kriterleri

- Uygulama başlangıçta crash olur veya eksik runtime/COM hatasını açıklayamaz.
- Installer secret/connection string ister veya kullanıcı verisini sessizce siler.
- Agent uzaktan erişime/firewall’a otomatik açılır.
- Upgrade/rollback yarım kurulum bırakır.
- Paket veya loglarda hassas veri bulunur.

## Sonuç kaydı

VM snapshot adı, Windows build’i, .NET runtime sürümü, installer/manifest version, her senaryonun PASS/FAIL sonucu, ekran görüntüleri ve log hash’leri kaydedilmelidir. Canlı doğrulama yapılmadıysa sonuç **PENDING** olarak kalır.