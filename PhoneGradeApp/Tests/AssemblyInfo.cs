using Xunit;

// Test classes are run in parallel by default, and this suite cannot tolerate
// that. Several classes mutate process-wide state that every other class can
// see: AUTODYMO_SETTINGS_DIR and AUTODYMO_LOG_DIR are read at call time rather
// than cached, UsbEventWatcher keeps its watchers in static fields, and each
// MainWindowViewModel binds port 5055. One class's constructor pointing the
// settings directory at its own temporary folder made a concurrently running
// class load "System" instead of "Dark", and a concurrently appending writer
// made the log assertions race. Running the classes one at a time costs a few
// seconds and makes the suite deterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
