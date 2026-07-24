# Netvium Identity API

Bu servis masaüstü uygulamasından ayrı çalışır. Veritabanı ve JWT sırları dosyalara yazılmaz.

Yerel geliştirme yapılandırması:

```powershell
dotnet user-secrets --project .\Netvium.Identity.Api set "ConnectionStrings:NetviumIdentity" "<local-development-value>"
dotnet user-secrets --project .\Netvium.Identity.Api set "Jwt:Issuer" "<issuer>"
dotnet user-secrets --project .\Netvium.Identity.Api set "Jwt:Audience" "<audience>"
dotnet user-secrets --project .\Netvium.Identity.Api set "Jwt:SigningKey" "<minimum-32-byte-random-value>"
```

Production ortamında aynı anahtarları environment variable/secret store ile sağlayın. API migration çalıştırmaz ve tablo oluşturmaz. `/health`, `dbo` şema sözleşmesini ve bağlantıyı sırları açığa çıkarmadan doğrular.
