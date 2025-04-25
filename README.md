# Lumina Language

Lumina is a lightweight dynamic programming language featuring:

- full math expression support everywhere  
- unrestricted variable naming (numbers, emojis, symbols)  
- live token deletion at runtime (even keywords and operators)  
- inline function calls  
- deep .NET interoperability via `IMPORT`  
- direct execution start (statements run as they appear)  
- **execution reversal** via a `REVERSE` statement  

*Now inspired by multiple ideas from [GulfOfMexico](https://github.com/TodePond/GulfOfMexico) (CREDITS).*

## Table of Contents

- [Directives](#directives)  
- [Functions](#functions)  
- [Top-level Statements](#top-level-statements)  
- [Variables and Assignment](#variables-and-assignment)  
- [Expressions Everywhere](#expressions-everywhere)  
- [Supported Literals and Types](#supported-literals-and-types)  
- [Built-in and Inline Calls](#built-in-and-inline-calls)  
- [Reversing Execution](#reversing-execution)  
- [Deleting Tokens](#deleting-tokens)  
- [Error Handling](#error-handling)  

---

## Directives

- `IMPORT "<path>"`  
  Load a .NET assembly for external function calls.

---

## Functions

- **Keyword**: any subsequence of **FUNCTION** (case-insensitive):  
  e.g. `F`, `FN`, `FUNC`, `FCTION`, `FUNCTION`  
- **Syntax**:
  ```text
  <FnKeyword> <Name>
    …statements…
  END
  ```
- **Note**: functions are only executed when called; there is no automatic `MAIN` entrypoint.

---

## Top-level Statements

Any statement outside a function runs in program order:

```text
!PRINTLINE "Hello"
x: 10
!PRINTLINE x*2
```

---

## Variables and Assignment

- **Arbitrary names**: numbers, emojis, symbols—anything.  
  ```text
  😂: "hello"
  3:   55
  $x!: 42
  ```
- **Colon syntax**:
  ```text
  VariableName: <expression>
  ```

---

## Expressions Everywhere

- Operators: `+`, `-`, `*`, `/`, `%`  
- Parentheses: `(` `)`  
- Appear in assignments, inline calls, `RETURN`, etc.

---

## Supported Literals and Types

- **Numbers**: e.g. `123`  
- **Strings**: `"hello world"`  
- **Booleans**: `TRUE`, `FALSE`

---

## Built-in and Inline Calls

- **Inline call**:
  ```text
  !FunctionName <expr> [<expr> …]
  ```
- **Built-in**:
  - `!PRINTLINE <value>` — prints evaluated value(s) to console  

- **External**:
  - Public static methods in imported .NET assemblies

---

## Reversing Execution

- **`REVERSE`**  
  Toggles execution direction of top-level statements:

  ```text
  !PRINTLINE 1
  !PRINTLINE 2
  REVERSE
  !PRINTLINE 3
  !PRINTLINE 4
  ```

  runs in order: 1,2 then toggles, then 4,3.

---

## Deleting Tokens

- `DELETE <token>`  
  - If a **variable** named `<token>` exists → deletes it only.  
  - Else → globally disables `<token>` forever.  

Disabled tokens cannot be used again as:
- Keywords (`RETURN`, `REVERSE`, function keywords, etc.)  
- Operators (`+`, `-`, etc.)  
- Function names  
- Literals (`"hello"`, `123`, etc.)  
- Variables  
- Anything else  

**Examples**:
```text
3: 55
DELETE 3
!PRINTLINE 3      # prints literal 3

DELETE RETURN
# further RETURN statements error

DELETE +
# '+' operator is now invalid

DELETE FN
# you can no longer define new functions
```

---

## Error Handling

Any error—undefined or deleted token, bad syntax, divide-by-zero, unknown function—  
aborts execution with an error message.