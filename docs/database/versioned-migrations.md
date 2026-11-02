# Migraciones versionadas (T-09)

## Alcance y estado

- SQL Server y PostgreSQL tienen historiales y snapshots **independientes**, en
  `CleanTemplate.Migrations.SqlServer` y `CleanTemplate.Migrations.PostgreSql`.
- Ambos proyectos referencian Infrastructure; Host referencia ambos para distribuir
  las migraciones. Infrastructure selecciona el assembly según `DatabaseProvider`.
  No se comparten migraciones entre dialectos.
- SQLite solo permite creación efímera en memoria en Development/Testing. No es
  un motor productivo ni tiene historial de migraciones soportado.
- La migración inicial incluye Identity, productos, refresh tokens y la tabla
  `ExternalRoleGrants` presente en los cambios locales de T-03. Si ese modelo cambia
  antes de publicar, revisar la migración y ambos snapshots. Una vez publicada,
  añadir migraciones nuevas: no reescribir un historial aplicado.

El arranque runtime no ejecuta migraciones ni bootstrap para bases persistentes.
Véase [operaciones de despliegue](deployment-operations.md) para comandos, permisos
y límites. No se usa `EnsureCreated` para SQL Server/PostgreSQL.

## Generar y revisar scripts sin arrancar la API

Desde la raíz del repositorio (PowerShell), con el SDK .NET 10 y `dotnet-ef` 10.x:

```powershell
dotnet ef migrations script --idempotent --project src/CleanTemplate.Migrations.SqlServer --startup-project src/CleanTemplate.Migrations.SqlServer --output sqlserver-upgrade.sql
dotnet ef migrations script --idempotent --project src/CleanTemplate.Migrations.PostgreSql --startup-project src/CleanTemplate.Migrations.PostgreSql --output postgresql-upgrade.sql
```

Los factories no necesitan configuración del Host, clave JWT ni contraseña de
administrador. Para generar scripts no se necesita conectar a una base. Revisar
los scripts y almacenarlos como artefactos de despliegue, no como secretos.

## Base vacía

1. Crear una base vacía con el motor elegido. Realizar un backup si no es desechable.
2. Revisar y ejecutar **solo** el script del proveedor correcto, con una cuenta de
   despliegue autorizada. Los scripts son idempotentes mediante `__EFMigrationsHistory`.
3. Verificar las tablas, índices, claves foráneas e historial; volver a ejecutar el
   script y confirmar que no altera datos ni duplica entradas del historial.
4. Ejecutar el bootstrap explícito si corresponde y configurar la API con el mismo motor y base, siguiendo la guía de operaciones.

En una base **aislada de pruebas** se puede usar directamente EF:

```powershell
# Cargar la conexión desde un almacén de secretos, no escribirla en el repositorio.
# La variable ConnectionStrings__DefaultConnection es leída por el factory.
dotnet ef database update --project src/CleanTemplate.Migrations.PostgreSql --startup-project src/CleanTemplate.Migrations.PostgreSql
```

Para SQL Server sustituir ambos nombres de proyecto por `CleanTemplate.Migrations.SqlServer`.
No pasar credenciales con `--connection` en terminales/historiales compartidos.

## Siguientes versiones

Después de modificar el modelo, generar **una migración por proveedor** con el mismo
nombre funcional y revisar diferencias:

```powershell
dotnet ef migrations add NombreDelCambio --project src/CleanTemplate.Migrations.SqlServer --startup-project src/CleanTemplate.Migrations.SqlServer --output-dir Migrations
dotnet ef migrations add NombreDelCambio --project src/CleanTemplate.Migrations.PostgreSql --startup-project src/CleanTemplate.Migrations.PostgreSql --output-dir Migrations
```

Antes de publicar: restaurar un backup de la versión anterior en una base aislada,
aplicar el script, comprobar datos e invariantes, repetirlo y ejecutar smoke tests.
Evaluar locks, duración y compatibilidad con réplicas que ejecuten la versión anterior.
Preferir expand/contract para cambios incompatibles. Un `Down` no es un backup:
el de la migración inicial elimina tablas y datos; no utilizarlo como recuperación.

## Instalaciones existentes creadas con EnsureCreated

**No ejecutar InitialCreate directamente sobre esas bases. No insertar filas en
`__EFMigrationsHistory` para fingir que la migración ya se aplicó.** EF no verifica
que el esquema existente coincida con el snapshot; el intento de crear tablas ya
existentes debe fallar y requiere intervención, no un fallback automático.

Ruta recomendada: migración lado a lado con ventana de mantenimiento.

1. Inventariar versión, tablas, columnas, tipos, índices, restricciones y volumen.
   Hacer y probar la restauración de un backup. Detener escrituras durante el corte.
2. Crear una base nueva y aplicar el script inicial del proveedor.
3. Preparar una transferencia específica para la instalación, con columnas explícitas
   y orden por dependencias. Preservar IDs, hashes de contraseña, stamps, logins,
   roles y claims. La copia de IDs de claims requiere tratar las secuencias/identity
   del motor. Revisar especialmente emisores de logins antiguos (T-01): copiar datos
   no demuestra confianza ni autoriza su vinculación automática.
4. Definir si se invalidan sesiones o se transfieren hashes/expiraciones de refresh
   tokens. Nunca convertir hashes en tokens ni omitir silenciosamente sesiones.
   Las tablas nuevas, como grants externos si no existían, requieren una decisión
   explícita; no inventar concesiones a partir de roles sin evidencia de procedencia.
5. Validar conteos y claves, muestreos de datos, referencias, acceso administrativo,
   autenticación y lecturas/escrituras de productos. Conservar evidencia sin PII.
6. Cambiar la conexión y ejecutar las pruebas de humo. Mantener el origen sin
   escrituras para rollback. Volver al origen solo es seguro si no se aceptaron
   escrituras nuevas o existe reconciliación aprobada.

No se suministra una conversión universal: el esquema de una instalación sin
historial no puede inferirse con seguridad. El procedimiento debe ensayarse sobre
un clon antes del corte; el ensayo automatizado descrito abajo no acredita una
instalación existente concreta.

## Evidencia y límites de aceptación

Las pruebas `VersionedMigrationsTests` verifican separación de assemblies, snapshot
actual, generación SQL de creación/upgrade idempotente/rollback y restricciones
del inicializador SQLite. Generar SQL **no demuestra** ejecución en motor real.

El 2 de noviembre de 2026 Docker respondió con daemon 29.8.2. La ejecución de
`./scripts/tests/Test-RefreshTokenRotation.ps1` superó 25/25 pruebas en SQL Server
2022 y 25/25 en PostgreSQL 17, sin omisiones. Esas pruebas usan contenedores propios,
bases aisladas y migraciones EF reales, incluyendo la actualización del esquema
previo de sesiones con datos; no ejecutan los scripts idempotentes ni ensayan el
traslado EnsureCreated. La solución `dotnet test CleanArchitectureTemplate.slnx
-c Release --nologo` superó 240 pruebas, sin fallos ni omisiones (7 Domain, 19
Application, 38 Architecture, 108 Infrastructure y 68 API).

El 2 de noviembre de 2026 `./scripts/tests/Test-VersionedMigrations.ps1` ejecutó
secuencialmente `VersionedMigrationsAcceptanceTests`: **4/4 SQL Server 2022 y 4/4
PostgreSQL 17**, sin fallos ni omisiones. Cada ejecución usa un contenedor nuevo y
bases de nombre aleatorio propias, que elimina al finalizar. Los scripts proceden
de `IMigrator`; el ejecutor separa lotes `GO` para SQL Server. Se comprueban creación,
reaplicación, upgrade desde InitialCreate con usuario y datos, fallo por índice único
de emails, historial sin falsificar, corrección y recuperación, y unicidad posterior.

El ensayo lado a lado crea el origen con EnsureCreated y el destino con el script,
transfiere con columnas explícitas un producto y un usuario Identity, y verifica
identificadores, datos del producto, email normalizado, hash y security stamp. No
transfiere roles, claims, logins, tokens, grants ni secuencias; tampoco representa
versiones antiguas arbitrarias, volúmenes reales, autenticación o corte operativo.
No es una conversión universal ni una certificación productiva.

`dotnet test CleanArchitectureTemplate.slnx -c Release --nologo` superó 240 pruebas,
sin fallos, con 4 omitidas: son las cuatro pruebas de aceptación real que requieren
el ejecutor anterior (ejecutadas sin omisión en cada motor). Desglose: 7 Domain,
19 Application, 38 Architecture, 108 Infrastructure y 68 API. Esta evidencia corresponde a T-09; para T-10 véase la guía de operaciones.
