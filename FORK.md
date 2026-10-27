# Fork additions

This fork adds sanitised source for:

- an untethered Tizen 6.0 boot chain (clean TPK + `ghservice`-slot stub +
  archive-root agent + token-gated relay);
- a generic UEP/SFD state-word tool where the platform-specific physical page
  and offset are supplied at runtime.

See [`contrib/README.md`](contrib/README.md) for the structure, the tested
device/build, what was deliberately removed, and the safety warnings.

The upstream project remains [`chris-ritsen/samsung-tv-root`](https://github.com/chris-ritsen/samsung-tv-root);
these additions are under `contrib/` and do not change the upstream payload
sources.
