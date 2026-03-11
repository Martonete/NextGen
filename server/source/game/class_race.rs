// PlayerClass and PlayerRace enums — type-safe replacements for String-based
// class/race comparisons scattered across the codebase.

use std::fmt;

/// All 12 character classes in VB6 13.3 (eClass enum order).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum PlayerClass {
    Mago,
    Clerigo,
    Guerrero,
    Asesino,
    Ladron,
    Bardo,
    Druida,
    Bandido,
    Paladin,
    Cazador,
    Trabajador,
    Pirata,
}

/// All 5 character races in VB6 13.3 (eRaza enum order).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum PlayerRace {
    Humano,
    Elfo,
    ElfoOscuro,
    Enano,
    Gnomo,
}

// ── PlayerClass ─────────────────────────────────────────────────────

impl PlayerClass {
    /// Parse from string (case-insensitive). Returns None for unrecognised input.
    pub fn from_str_opt(s: &str) -> Option<Self> {
        let upper = s.to_uppercase();
        match upper.as_str() {
            "MAGO" => Some(Self::Mago),
            "CLERIGO" | "CLÉRIGO" => Some(Self::Clerigo),
            "GUERRERO" => Some(Self::Guerrero),
            "ASESINO" => Some(Self::Asesino),
            "LADRON" | "LADRÓN" => Some(Self::Ladron),
            "BARDO" => Some(Self::Bardo),
            "DRUIDA" => Some(Self::Druida),
            "BANDIDO" => Some(Self::Bandido),
            "PALADIN" | "PALADÍN" => Some(Self::Paladin),
            "CAZADOR" => Some(Self::Cazador),
            "TRABAJADOR" => Some(Self::Trabajador),
            "PIRATA" => Some(Self::Pirata),
            _ => None,
        }
    }

    /// Parse from string, falling back to Guerrero if unrecognised.
    pub fn from_str_or_default(s: &str) -> Self {
        Self::from_str_opt(s).unwrap_or(Self::Guerrero)
    }

    /// 0-based index matching VB6 eClass and `balance::class_id` constants.
    pub fn index(self) -> usize {
        match self {
            Self::Mago => 0,
            Self::Clerigo => 1,
            Self::Guerrero => 2,
            Self::Asesino => 3,
            Self::Ladron => 4,
            Self::Bardo => 5,
            Self::Druida => 6,
            Self::Bandido => 7,
            Self::Paladin => 8,
            Self::Cazador => 9,
            Self::Trabajador => 10,
            Self::Pirata => 11,
        }
    }

    // ── Classification helpers ──────────────────────────────────────

    /// True for Mago, Clerigo, Druida, Bardo (classes with significant mana).
    pub fn is_mage(self) -> bool {
        matches!(self, Self::Mago | Self::Clerigo | Self::Druida | Self::Bardo)
    }

    /// True for Trabajador (resource-gathering class).
    pub fn is_recolector(self) -> bool {
        self == Self::Trabajador
    }

    /// True for Paladin and Clerigo (holy/noble classes).
    pub fn is_noble(self) -> bool {
        matches!(self, Self::Paladin | Self::Clerigo)
    }

    /// True for Asesino, Bandido, Pirata, Ladron (outlaw classes).
    pub fn is_criminal_class(self) -> bool {
        matches!(self, Self::Asesino | Self::Bandido | Self::Pirata | Self::Ladron)
    }

    /// True for Ladron or Bandido (stealth-capable classes for hiding).
    pub fn is_thief_or_bandit(self) -> bool {
        matches!(self, Self::Ladron | Self::Bandido)
    }
}

impl fmt::Display for PlayerClass {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let s = match self {
            Self::Mago => "Mago",
            Self::Clerigo => "Clerigo",
            Self::Guerrero => "Guerrero",
            Self::Asesino => "Asesino",
            Self::Ladron => "Ladron",
            Self::Bardo => "Bardo",
            Self::Druida => "Druida",
            Self::Bandido => "Bandido",
            Self::Paladin => "Paladin",
            Self::Cazador => "Cazador",
            Self::Trabajador => "Trabajador",
            Self::Pirata => "Pirata",
        };
        write!(f, "{}", s)
    }
}

impl Default for PlayerClass {
    fn default() -> Self {
        Self::Guerrero
    }
}

// ── PlayerRace ──────────────────────────────────────────────────────

impl PlayerRace {
    /// Parse from string (case-insensitive). Returns None for unrecognised input.
    pub fn from_str_opt(s: &str) -> Option<Self> {
        let upper = s.to_uppercase();
        match upper.as_str() {
            "HUMANO" => Some(Self::Humano),
            "ELFO" => Some(Self::Elfo),
            "ELFO OSCURO" | "ELFOOSCURO" | "DROW" => Some(Self::ElfoOscuro),
            "ENANO" => Some(Self::Enano),
            "GNOMO" => Some(Self::Gnomo),
            _ => None,
        }
    }

    /// Parse from string, falling back to Humano if unrecognised.
    pub fn from_str_or_default(s: &str) -> Self {
        Self::from_str_opt(s).unwrap_or(Self::Humano)
    }

    /// 0-based index matching VB6 eRaza and `balance::race_id` constants.
    pub fn index(self) -> usize {
        match self {
            Self::Humano => 0,
            Self::Elfo => 1,
            Self::ElfoOscuro => 2,
            Self::Enano => 3,
            Self::Gnomo => 4,
        }
    }

    /// Canonical Spanish display name (for DB storage and protocol).
    pub fn canonical_name(self) -> &'static str {
        match self {
            Self::Humano => "Humano",
            Self::Elfo => "Elfo",
            Self::ElfoOscuro => "Elfo Oscuro",
            Self::Enano => "Enano",
            Self::Gnomo => "Gnomo",
        }
    }
}

impl fmt::Display for PlayerRace {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}", self.canonical_name())
    }
}

impl Default for PlayerRace {
    fn default() -> Self {
        Self::Humano
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn class_from_str() {
        assert_eq!(PlayerClass::from_str_opt("guerrero"), Some(PlayerClass::Guerrero));
        assert_eq!(PlayerClass::from_str_opt("MAGO"), Some(PlayerClass::Mago));
        assert_eq!(PlayerClass::from_str_opt("Clérigo"), Some(PlayerClass::Clerigo));
        assert_eq!(PlayerClass::from_str_opt("Ladrón"), Some(PlayerClass::Ladron));
        assert_eq!(PlayerClass::from_str_opt("unknown"), None);
    }

    #[test]
    fn race_from_str() {
        assert_eq!(PlayerRace::from_str_opt("humano"), Some(PlayerRace::Humano));
        assert_eq!(PlayerRace::from_str_opt("Elfo Oscuro"), Some(PlayerRace::ElfoOscuro));
        assert_eq!(PlayerRace::from_str_opt("DROW"), Some(PlayerRace::ElfoOscuro));
        assert_eq!(PlayerRace::from_str_opt("unknown"), None);
    }

    #[test]
    fn class_helpers() {
        assert!(PlayerClass::Mago.is_mage());
        assert!(PlayerClass::Druida.is_mage());
        assert!(!PlayerClass::Guerrero.is_mage());
        assert!(PlayerClass::Trabajador.is_recolector());
        assert!(PlayerClass::Ladron.is_thief_or_bandit());
        assert!(PlayerClass::Bandido.is_thief_or_bandit());
        assert!(!PlayerClass::Pirata.is_thief_or_bandit());
    }

    #[test]
    fn class_display() {
        assert_eq!(PlayerClass::Guerrero.to_string(), "Guerrero");
        assert_eq!(PlayerClass::Mago.to_string(), "Mago");
    }

    #[test]
    fn race_display() {
        assert_eq!(PlayerRace::ElfoOscuro.to_string(), "Elfo Oscuro");
        assert_eq!(PlayerRace::Humano.to_string(), "Humano");
    }

    #[test]
    fn class_index_matches_balance() {
        assert_eq!(PlayerClass::Mago.index(), 0);
        assert_eq!(PlayerClass::Guerrero.index(), 2);
        assert_eq!(PlayerClass::Pirata.index(), 11);
    }
}
