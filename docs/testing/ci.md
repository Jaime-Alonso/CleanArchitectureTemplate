# Calidad reproducible y CI (T-30)

`.github/workflows/ci.yml` queda preparado con activación manual exclusivamente
(`workflow_dispatch`). Por decisión del usuario, no se ejecuta en GitHub ni se
configuran protecciones de ramas para cerrar T-30; su aceptación es local en
Windows y Linux. No hay activadores automáticos por push, PR o calendario.
No despliega ni publica paquetes. Solo necesita permisos `contents: read`, sin
secretos del repositorio; no usa `pull_request_target`. Las acciones están fijadas
por SHA y las herramientas Docker por digest en `scripts/ci/tools.json`.

## Contrato de ejecución

| Job | Comprobaciones |
| --- | --- |
| Release (ubuntu-24.04 / windows-2025) | SDK exacto, UTF-8/finales de línea, restore bloqueado, formato global, build Release, auditoría directa/transitiva, pruebas ordinarias, cobertura y aislamiento frente a configuración ambiental inválida. |
| Real infrastructure | Diez pruebas opt-in con SQL Server, PostgreSQL y Redis mediante Docker. Docker inaccesible falla; no se convierte en skip. |
| Source security | actionlint, Gitleaks sin red durante el escaneo y detección obligatoria de un token sintético en un archivo de prueba. |
| CI required | Falla si cualquier job anterior falla, se cancela o se omite. Es el nombre estable para protección de ramas. |

**Configuración del repositorio en GitHub:** para impedir merges, el administrador
debe exigir el check **CI required** en el ruleset/protección de la rama destino.
Añadir un workflow no activa esa regla automáticamente ni impide que un
administrador con bypass la omita. La ejecución alojada y esa regla solo se comprobarán si se decide adoptar CI
remota en el futuro; quedan fuera de la aceptación actual. Las ejecuciones
locales no acreditan esos estados remotos. No hay filtros por rutas que dejen el check requerido pendiente.

Los resultados TRX, informes de formato, auditoría JSON y cobertura combinada
HTML/Cobertura/JSON se publican con `always()` y retención de 14 días. La tabla
de umbrales medidos se añade al resumen del job. Los informes del escáner están
redactados; no se publican valores encontrados ni credenciales de fixtures.
El operador decide quién puede acceder a los artefactos y si cambia la retención.

## SDK, dependencias y actualizaciones

- `global.json` fija SDK **10.0.403**, sin roll-forward ni versiones preliminares.
  Se conserva VSTest, requerido por `coverlet.collector`; no se activa MTP.
- `NuGet.Config` limita fuentes de paquetes y auditoría a NuGet oficial. Los 15
  proyectos de la solución conservan `packages.lock.json`; CI restaura con
  `--locked-mode`. Cambiar referencias sin actualizar los locks debe fallar.
- Se auditan todas las dependencias, incluidas transitivas, desde severidad Low.
  Los warnings son errores. El listado JSON tiene un control adicional porque
  `dotnet list package --vulnerable` puede finalizar correctamente con hallazgos.
  La indisponibilidad de la auditoría o un informe malformado también falla.
- ReportGenerator está fijado en `.config/dotnet-tools.json`. Gitleaks 8.30.1 y
  actionlint 1.7.12 usan imágenes con digest; sus versiones y digests se revisan
  juntos. Las acciones conservan un comentario con la versión del SHA fijado.
- Dependabot propone revisiones semanales de NuGet y acciones. El SDK y los
  digests de contenedores se revisan al menos mensualmente y ante avisos de
  seguridad; no se actualizan silenciosamente. Una actualización de SDK es un
  cambio revisable de `global.json`, validado en ambas plataformas.

Después de cambiar paquetes intencionadamente:

```powershell
dotnet restore CleanArchitectureTemplate.slnx --force-evaluate --configfile NuGet.Config
git diff -- '**/packages.lock.json'
```

La reproducibilidad cubre SDK, grafo resuelto y versiones de herramientas. Los
avisos de seguridad y las imágenes mantenidas de runners evolucionan: una
ejecución que hoy pasa puede fallar mañana. No se promete un entorno hermético
ni builds byte a byte idénticos entre sistemas operativos.

## Cobertura medida y objetivos

`coverage.runsettings` incluye los siete ensamblados runtime. Excluye ensamblados
de pruebas, migraciones generadas y código marcado con atributos explícitos de
código generado/exclusión; no excluye namespaces de seguridad ni los estados
async mediante `CompilerGeneratedAttribute`. Las migraciones se validan además
mediante las aceptaciones reales, fuera de la cifra de cobertura ordinaria.

La primera medición Windows de T-30 fue **2666/2939 líneas (90,71 %)** y
**1108/1436 ramas (77,16 %)**. Los números son ejecución medida, no estimaciones.
La copia limpia Linux produjo las mismas 2666/2939 líneas y 1107/1436 ramas
(77,09 %); ambas plataformas superaron los umbrales con 446 pruebas ordinarias
aprobadas y 14 omisiones de aceptaciones separadas. El script de aislamiento
ambiental repitió sus tres casos con éxito en ambas. Las diez pruebas reales
también pasaron, sin omisiones. La variación de una rama entre plataformas no
se oculta redondeando ni cambiando la política.
Los umbrales se calculan con los contadores sin redondear y están en
`scripts/ci/coverage-thresholds.json`:

| Código | Mínimo líneas | Mínimo ramas | Razón |
| --- | ---: | ---: | --- |
| Total runtime instrumentado | 85 % | 70 % | Evitar regresión amplia, con margen para diferencias de plataforma. |
| Selección/validación JWT e identidad interna | 95 % | 75 % | Mantener recorridos de confianza y rechazo; no equiparar cobertura con prueba de seguridad exhaustiva. |
| Validación de opciones de autenticación | 85 % | 85 % | Protección de combinaciones y límites al arrancar. |
| Provisioning transaccional | 95 % | 75 % | Proteger concurrencia y rechazos, manteniendo margen para fallos dependientes del proveedor. |
| RefreshTokenService | 95 % | 85 % | Rotación, revocación, expiración y rollback son rutas críticas. |
| Autorización persistida | 95 % | 95 % | Evitar fallback inseguro y regresión de revocación. |
| ValidationBehavior y Product | 95 % | 95 % | Proteger pipeline y decisiones de invariantes. |

La aceptación local se reprodujo con exportaciones independientes del árbol
fuente, sin copiar bin/obj ni cachés del workspace. Windows usó el SDK local;
Linux ejecutó la imagen SDK 10.0.403, digest
`sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e`.
Es evidencia local, no una ejecución de los runners alojados de GitHub. La
revisión detectó que la regla histórica `coverage*.json` ocultaba el archivo de
umbrales: la excepción específica en `.gitignore` permite versionarlo. También
se comprobó que un grafo cambiado contra un lock existente falla con NU1004.

No se permite que desaparezca una clase crítica del informe. Faltan métricas o
cualquiera de los cinco informes de proyecto: el job falla. Cada ejecución usa
un directorio nuevo para impedir que informes antiguos oculten un fallo del
collector; las copias de adjuntos TRX no se cuentan como sesiones adicionales.

**Límites:** son umbrales de regresión iniciales, no una garantía de corrección.
La suite ordinaria no sustituye las diez aceptaciones reales, obligatorias en
otro job, ni los ensayos históricos de migración/backup con opt-in propio. La
medición inicial ordinaria de SharedKernel fue 47,5 % de líneas y la de
UnitOfWork 30,9 %; sus ramas transaccionales se ejercitan principalmente en
aceptaciones separadas y no se les atribuye cobertura alta por sumar otras
clases. Quedan como deuda explícita de cobertura, sin excluirlas del total.
Los hijos Host que se terminan por fuerza y las operaciones externas tampoco
quedan íntegramente acreditados por un informe de cobertura del testhost.

## Secretos y finales de línea

Gitleaks analiza el árbol fuente actual con reglas oficiales y sin exclusión
global de tests. Las únicas exclusiones son metadatos Git/IDE y salidas locales
o generadas (`bin`, `obj`, `work`, `artifacts`). No se usa baseline de hallazgos
para aceptar secretos nuevos ni comentarios `gitleaks:allow` para omitirlos.
El canario es sintético y nunca se usa como credencial. No es un escaneo del
historial Git ni una certificación de ausencia de secretos.

`.gitattributes` y `.editorconfig` acuerdan LF para texto y CRLF para C#.
`Test-TextFiles.ps1` comprueba UTF-8 sin BOM, newline final y esos finales de línea;
`dotnet format --verify-no-changes` comprueba el estilo de toda la solución. No
es necesario cambiar `core.autocrlf` global ni reformatear en CI.

## Reproducir localmente

Desde la raíz, con SDK exacto y PowerShell 7:

```powershell
pwsh -NoProfile -File scripts/tests/Test-QualityGates.ps1
pwsh -NoProfile -File scripts/ci/Invoke-Quality.ps1
pwsh -NoProfile -File scripts/ci/Test-SourceSecurity.ps1
dotnet test test/CleanTemplate.Infrastructure.RealTests/CleanTemplate.Infrastructure.RealTests.csproj -c Release --no-restore -p:RunInfrastructureReal=true --filter Category=InfrastructureReal
```

La cadena ordinaria necesita NuGet y HTTP de loopback. El escáner y las pruebas
reales requieren Docker Linux y descargar las imágenes inicialmente. Las pruebas
del control de calidad inyectan baja/ausente cobertura, ramas críticas no
cubiertas, vulnerabilidad transitiva, error de auditoría y JSON inválido: deben
ser rechazados. El escáner comprueba un canario de extremo a extremo. No se
publican resultados ni se cambian reglas de GitHub al ejecutar estos comandos.
