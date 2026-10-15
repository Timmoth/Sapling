// Entry point for the Sapling browser-wasm web host.
//
// The engine is driven from a dedicated Web Worker through the [JSExport] bindings on
// EngineHost. Main intentionally performs no work: the worker creates the runtime,
// resolves the exported host object, and then calls EngineHost methods directly. Keeping
// Main trivial avoids any stdin/console/process assumptions on the browser path.
return 0;
