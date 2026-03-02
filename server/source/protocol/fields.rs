/// Field parsing utilities.
///
/// Mirrors VB6: `ReadField(Pos, text, SepASCII)` from General.bas
///
/// The VB6 ReadField extracts the Nth field from a delimited string.
/// Fields are 1-based. Common delimiters:
///   44  = ',' (comma) — most packet fields
///   176 = '░' (extended ASCII) — character data
///   126 = '~' (tilde) — UI/color data
///   45  = '-' (dash) — ID delimiter

/// Extract field at position `pos` (1-based) from `text` using delimiter `sep`.
///
/// Returns empty string if the field doesn't exist.
pub fn read_field(pos: usize, text: &str, sep: char) -> String {
    if pos == 0 {
        return String::new();
    }

    let mut current_field = 1;
    let mut start = 0;

    for (i, ch) in text.char_indices() {
        if ch == sep {
            if current_field == pos {
                return text[start..i].to_string();
            }
            current_field += 1;
            start = i + ch.len_utf8();
        }
    }

    // Last field (no trailing separator)
    if current_field == pos {
        return text[start..].to_string();
    }

    String::new()
}

/// Convenience trait for field parsing.
pub trait ReadField {
    fn read_field(&self, pos: usize, sep: char) -> String;
    fn read_field_int(&self, pos: usize, sep: char) -> i32;
    fn read_field_long(&self, pos: usize, sep: char) -> i64;
}

impl ReadField for str {
    fn read_field(&self, pos: usize, sep: char) -> String {
        read_field(pos, self, sep)
    }

    fn read_field_int(&self, pos: usize, sep: char) -> i32 {
        read_field(pos, self, sep).parse().unwrap_or(0)
    }

    fn read_field_long(&self, pos: usize, sep: char) -> i64 {
        read_field(pos, self, sep).parse().unwrap_or(0)
    }
}

impl ReadField for String {
    fn read_field(&self, pos: usize, sep: char) -> String {
        read_field(pos, self, sep)
    }

    fn read_field_int(&self, pos: usize, sep: char) -> i32 {
        read_field(pos, self, sep).parse().unwrap_or(0)
    }

    fn read_field_long(&self, pos: usize, sep: char) -> i64 {
        read_field(pos, self, sep).parse().unwrap_or(0)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn basic_comma_separated() {
        let text = "field1,field2,field3";
        assert_eq!(read_field(1, text, ','), "field1");
        assert_eq!(read_field(2, text, ','), "field2");
        assert_eq!(read_field(3, text, ','), "field3");
    }

    #[test]
    fn out_of_bounds() {
        let text = "a,b";
        assert_eq!(read_field(3, text, ','), "");
        assert_eq!(read_field(0, text, ','), "");
    }

    #[test]
    fn single_field() {
        let text = "hello";
        assert_eq!(read_field(1, text, ','), "hello");
        assert_eq!(read_field(2, text, ','), "");
    }

    #[test]
    fn empty_fields() {
        let text = "a,,c";
        assert_eq!(read_field(1, text, ','), "a");
        assert_eq!(read_field(2, text, ','), "");
        assert_eq!(read_field(3, text, ','), "c");
    }

    #[test]
    fn tilde_separator() {
        // Color data: "text~255~128~0~1~0"
        let text = "Hello World~255~128~0~1~0";
        assert_eq!(read_field(1, text, '~'), "Hello World");
        assert_eq!(read_field(2, text, '~'), "255");
        assert_eq!(read_field(3, text, '~'), "128");
    }

    #[test]
    fn trait_usage() {
        let text = "100,200,300";
        assert_eq!(text.read_field(1, ','), "100");
        assert_eq!(text.read_field_int(2, ','), 200);
        assert_eq!(text.read_field_long(3, ','), 300);
    }
}
