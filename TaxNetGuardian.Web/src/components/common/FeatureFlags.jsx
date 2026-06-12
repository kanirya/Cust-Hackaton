import React, { createContext, useContext } from "react";

// Lightweight feature-flag context. App fetches flags from /api/feature-flags and provides
// them here; any component can gate UI with useFlag("some.key").
const FlagsContext = createContext({});

function FlagsProvider({ value, children }) {
  return <FlagsContext.Provider value={value || {}}>{children}</FlagsContext.Provider>;
}

// Returns the flag value; defaults to `fallback` (true) when the flag isn't loaded yet/unknown,
// so the UI never disappears while flags are loading.
function useFlag(key, fallback = true) {
  const flags = useContext(FlagsContext);
  return Object.prototype.hasOwnProperty.call(flags, key) ? !!flags[key] : fallback;
}

export { FlagsProvider, useFlag };
