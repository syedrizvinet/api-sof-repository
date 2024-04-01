namespace Terrajobst.ApiCatalog;

public enum MarkupTokenKind : byte
{
    None,

    // Whitespace,
    LineBreak,
    Space,

    // Literals
    LiteralString,
    LiteralNumber,

    // Reference
    ReferenceToken,

    // Punctuation
    AmpersandToken,
    AsteriskToken,
    BarToken,
    CaretToken,
    CloseBraceToken,
    CloseBracketToken,
    CloseParenToken,
    ColonToken,
    CommaToken,
    DotToken,
    EqualsEqualsToken,
    EqualsToken,
    ExclamationEqualsToken,
    ExclamationToken,
    GreaterThanEqualsToken,
    GreaterThanGreaterThanGreaterThanToken,
    GreaterThanGreaterThanToken,
    GreaterThanToken,
    LessThanEqualsToken,
    LessThanLessThanToken,
    LessThanToken,
    MinusMinusToken,
    MinusToken,
    OpenBraceToken,
    OpenBracketToken,
    OpenParenToken,
    PercentToken,
    PlusPlusToken,
    PlusToken,
    QuestionToken,
    SemicolonToken,
    SlashToken,
    TildeToken,
    
    // Keywords
    AbstractKeyword,
    AddKeyword,
    BoolKeyword,
    ByteKeyword,
    CdeclKeyword,
    CharKeyword,
    ClassKeyword,
    ConstKeyword,
    DecimalKeyword,
    DefaultKeyword,
    DelegateKeyword,
    DoubleKeyword,
    DynamicKeyword,
    EnumKeyword,
    EventKeyword,
    ExplicitKeyword,
    FalseKeyword,
    FastcallKeyword,
    FloatKeyword,
    GetKeyword,
    ImplicitKeyword,
    InKeyword,
    IntKeyword,
    InterfaceKeyword,
    InternalKeyword,
    LongKeyword,
    NamespaceKeyword,
    NewKeyword,
    NotnullKeyword,
    NullKeyword,
    ObjectKeyword,
    OperatorKeyword,
    OutKeyword,
    OverrideKeyword,
    ParamsKeyword,
    PrivateKeyword,
    ProtectedKeyword,
    PublicKeyword,
    ReadonlyKeyword,
    RefKeyword,
    RemoveKeyword,
    ReturnKeyword,
    SbyteKeyword,
    SealedKeyword,
    SetKeyword,
    ShortKeyword,
    StaticKeyword,
    StdcallKeyword,
    StringKeyword,
    StructKeyword,
    ThisKeyword,
    ThiscallKeyword,
    TrueKeyword,
    TypeofKeyword,
    UintKeyword,
    UlongKeyword,
    UnmanagedKeyword,
    UshortKeyword,
    VirtualKeyword,
    VoidKeyword,
    VolatileKeyword,
    WhereKeyword,
}