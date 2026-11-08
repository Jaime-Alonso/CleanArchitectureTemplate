# Pruebas negativas y aceptación de Production (T-29)

La plantilla se valida para proyectos nuevos. No se exige conservar contratos,
clientes o instalaciones anteriores a su primera publicación. Las pruebas de
transacciones, restricciones, revocación y migraciones siguen protegiendo la
integridad de los proyectos que se generen.

## Hosts y aislamiento

`test/CleanTemplate.Api.IntegrationTests` arranca el entry point real con
`TestWebApplicationFactory`, en `Testing` por defecto. Cada factoría independiente
tiene una base SQLite en memoria con nombre único y una clave JWT aleatoria.
Un `IHostingStartup` exclusivo del ensamblado de pruebas sustituye los proveedores
por configuración en memoria dentro de `CreateBuilder`, antes de que Program lea
opciones o registre servicios. La factoría lo selecciona explícitamente y captura
el snapshot mediante un AsyncLocal restaurado al terminar el arranque; no hay
hooks ni ramas de prueba en el Host productivo. La configuración final también
se sustituye al construir el host. No se depende
de appsettings, user-secrets ni variables de entorno del desarrollador, y las
pruebas no modifican variables de entorno del proceso que las ejecuta.

Los cambios de opciones leídas durante el arranque se pasan al constructor de la
factoría; `WithWebHostBuilder` se reserva para sustituir servicios. Los clones de
una misma factoría conservan su configuración y base: para aislar datos se crea
otra factoría. `HostIsolationTests` comprueba proveedores exclusivamente en
memoria, bases independientes y rechazo de tokens firmados por el otro host.

Development se selecciona expresamente para probar OpenAPI y sus metadatos.
Las pruebas de errores y seguridad de Production conservan el entorno real
`Production`, con opt-in explícito de SQLite efímero. Este opt-in solo facilita
pruebas HTTP aisladas: no demuestra el despliegue de una base persistente.
`ProductionEphemeralOptIn_CreatesSchemaWithoutBootstrapOrDevelopmentRoutes`
comprueba esquema, ausencia de usuarios/roles automáticos, probes, protección de
productos y ausencia de OpenAPI/Scalar sin contraseña administrativa.
Otro escenario comprueba que Staging no crea el esquema y que readiness solo
comprueba conectividad: puede estar Healthy aunque aún no existan tablas.

El bootstrap de Testing y Development utiliza credenciales exclusivamente de
fixture. En Production los usuarios y roles necesarios se crean explícitamente
en cada escenario. Se mantienen la autenticación JWT, autorización interna y
persistencia reales; los dobles que inyectan fallos se limitan a esos escenarios.

El bootstrap Serilog y la captura de stdout tienen estado global: las colecciones
de hosts/consola siguen sin paralelismo. Las pruebas de proceso hijo configuran
solo el entorno del hijo, conservan el control de código de salida y secretos,
y requieren conexiones HTTP de loopback. Las restricciones de red del entorno de
ejecución deben permitirlas; un fallo no se convierte en skip.

## Trazabilidad de los escenarios de la sección 6 del plan

| Área | Regresiones y límites |
| --- | --- |
| Identidad externa | `ExternalIdentityProvisioningServiceTests`, `TransactionalProvisioningTests`, `InternalAuthorizationTests` y `OidcTrustConfigurationTests`: colisiones de correo, verificación, issuer/subject/tenant, concurrencia y rollback. Se usan tokens y metadatos controlados; no se acredita un IdP externo desplegado. |
| Autorización | `ProductAuthorizationTests`, `InternalAuthorizationTests`, `RoleRevocationEndpointsTests`, `RoleSynchronizationTests` y `ProductionSecurityTests`: 401/403, claims internos falsificados, retirada de roles y fallo cerrado sin fallback a roles del token o Redis. |
| JWT | `AuthenticationSchemeTests`, `AuthOptionsValidationTests` y `ProductionSecurityTests`: firma, issuer, audience, expiración, not-before, token sin firma/malformado, esquemas deshabilitados y solapamiento/retirada de claves. La emisión comprueba el kid activo. |
| Sesiones | `RefreshTokenRotationTests` y `SessionEndpointsTests`: consumo concurrente, replay, expiración, cancelación/rollback, logout, familias, security stamp y plazo absoluto. `RefreshRotationRealTests` comprueba la carrera en ambos motores. Los access tokens ya emitidos conservan los límites documentados. |
| Datos | `ProductTests`, pruebas de handlers, `ProductEndpointsTests`, `ProductAuthorizationTests`, pruebas de unidad de trabajo y `DatabaseRealTests`: límites, decimales, 409 por versión obsoleta, transacciones y lectura primaria/paginación. No se acredita una réplica operativa. |
| Migraciones | `DatabaseRealTests` aplica migraciones sobre bases vacías. Las aceptaciones de actualización, fallos y recuperación existentes conservan sus scripts opt-in; no se añaden exigencias de compatibilidad con instalaciones históricas ni despliegues de binarios antiguos. |
| HTTP | `ProblemDetailsProductionTests`, `ApiVersionTests`, `DocumentationEndpointsTests` y el smoke de cliente: errores, cabeceras, pertenencia real de operaciones a versiones, OpenAPI nativo y seguridad. Sin preservación de esquemas históricos. |
| Operación | `StartupProcessTests`, `HealthTests`, `TrafficProtectionTests`, pruebas de configuración, observabilidad y cleanup: fallo de arranque, Production, probes, proxies, 429, dependencias caídas y apagado cooperativo. No se acredita un ingress, TLS o backend de observabilidad de producción. |
| Plantilla | La generación con otro nombre, build/test/publish y matriz de capacidades siguen en T-32/T-33. T-29 no acredita un paquete dotnet new todavía inexistente. |

La clase anterior `AuthAndProductsEndpointsTests` se reparte en
`LoginEndpointsTests`, `SessionEndpointsTests`, `ProductEndpointsTests`,
`LoginRateLimitTests` y `DocumentationEndpointsTests`. Los helpers HTTP y DTO de
prueba compartidos están en `EndpointTestBase`; no cambian los contratos runtime.

## Ejecución

Suite ordinaria, sin Docker:

```sh
dotnet test CleanArchitectureTemplate.slnx -c Release
```

Aceptación del aislamiento frente a valores ambientales inválidos, sin modificar
el proceso del desarrollador y sin Docker:

```powershell
pwsh -NoProfile -File scripts/tests/Test-HttpHostIsolation.ps1
```

El script introduce en el runner hijo un proveedor/conexiones inválidos, una clave
rotada incompleta, un proxy inválido y valores numéricos malformados. Ejecuta
`HostIsolationTests`; los hosts deben arrancar con su snapshot propio. Esto evita
que una sustitución tardía de configuración oculte contaminación durante el
preflight y los registros. El script tiene timeout y propaga los fallos.

Regresión incluyendo las diez pruebas aisladas con infraestructura real:

```sh
dotnet test CleanArchitectureTemplate.slnx -c Release -p:RunInfrastructureReal=true
```

Las aceptaciones históricas de motores, despliegue y backup/restore mantienen su
activación propia. Las omisiones de esas aceptaciones en la suite ordinaria no
equivalen a una ejecución satisfactoria. Véanse [infraestructura real](../../test/CleanTemplate.Infrastructure.RealTests/README.md),
[migraciones](../database/versioned-migrations.md) y [recuperación](../recovery/operations.md).
