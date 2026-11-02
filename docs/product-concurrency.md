# Concurrencia optimista de productos

Las lecturas de detalle y listado incluyen `version` (entero de 64 bits positivo). Un producto nuevo o migrado comienza en 1; cada actualización aceptada incrementa la versión, incluso si sus datos no cambian. La creación devuelve solo el identificador: hay que leer el producto antes de modificarlo.

* `PUT /api/products/{id}` requiere `expectedVersion` en el cuerpo JSON, junto con los demás campos.
* `DELETE /api/products/{id}?expectedVersion=1` requiere la versión en la cadena de consulta.
* Una versión ausente, no numérica o no positiva produce 400. Una versión obsoleta produce 409 con código HTTP `resource.conflict` en ProblemDetails (el código interno de Application sigue siendo `Products.VersionConflict`); el cliente debe volver a leer y decidir cómo resolver el conflicto. Un producto que no existe al iniciar la operación produce 404. Si desaparece o cambia entre lectura y escritura, se devuelve 409.
* No se utiliza ETag ni `If-Match`. Las actualizaciones anteriores sin versión y los borrados sin parámetro dejan de ser compatibles; actualizar clientes antes de habilitar el nuevo contrato.

EF usa la versión original como token de concurrencia en el predicado de actualización o borrado y convierte el fallo de concurrencia en conflicto; Application no depende de EF y Dapper sigue sirviendo lecturas. Esta garantía requiere que **todos** los escritores incrementen la versión y respeten el predicado; SQL directo u otros servicios que no lo hagan pueden eludirla. Los consumidores deben tratarla como un entero de 64 bits, no como contador JavaScript seguro sin límites.

Las nuevas migraciones por proveedor añaden `Version bigint NOT NULL DEFAULT 1` sin modificar el historial. Aplicarlas antes de desplegar código nuevo; coordinar escritores antiguos, que no respetan el contrato. SQLite se usa en el perfil efímero `EnsureCreated`; no se proporcionan migraciones SQLite persistentes.

## Evidencia

`pwsh -NoProfile -File scripts/tests/Test-VersionedMigrations.ps1`: SQL Server 2022 y PostgreSQL 17, contenedores y bases aislados; 5 pruebas de migración/persistencia y 3 de despliegue por proveedor. `ProductVersionUpgradePreservesExistingProductAndRejectsStaleWrites` migra desde la migración inmediatamente anterior con un producto existente y comprueba actualización y borrado obsoletos usando dos contextos. `SqliteIndependentContextsRejectStaleUpdateAndDelete` cubre el mismo patrón en SQLite. `Products_StaleClientsCannotUpdateOrDeleteNewerVersion` comprueba dos clientes HTTP con la misma versión, 204/409 y conservación de datos; las peticiones de la prueba HTTP son secuenciales, mientras que las pruebas de persistencia fuerzan la carrera entre lectura y guardado mediante contextos independientes.

Compilación Release de la solución: correcta, sin advertencias. `dotnet test CleanArchitectureTemplate.slnx -c Release --no-build --no-restore`: 244 superadas y 6 omitidas; las omitidas son las pruebas de aceptación que requieren el entorno de contenedores y se ejecutaron por separado como se indica arriba.

Responsable: opencode; revisión de Codex completada con las verificaciones documentadas. No se promete plazo para tareas posteriores.
