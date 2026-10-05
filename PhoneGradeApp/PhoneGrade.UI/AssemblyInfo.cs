using System.Runtime.CompilerServices;

// ============ What the test project may reach ============
//
// The pieces of the views that decide what is drawn rather than how it is laid out.
// They are internal because a view's drawing details are not part of its
// interface, and they are visible here because the fault they had was in the
// drawing and nothing else can see that: a barcode drawn as a run of characters
// passes every test that only asks whether the control is on screen.
[assembly: InternalsVisibleTo("Tests")]