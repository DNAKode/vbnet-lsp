const PREC = {
  call: 8,
  member: 9,
};

const commaSep = (rule) => optional(seq(rule, repeat(seq(',', rule))));

const keyword = (word) => {
  const chars = word
    .split('')
    .map((char) => `[${char.toLowerCase()}${char.toUpperCase()}]`)
    .join('');
  return token(new RegExp(chars));
};

module.exports = grammar({
  name: 'vbnet',

  extras: ($) => [
    /[ \t\f]+/,
    $.comment,
  ],

  word: ($) => $.identifier,

  rules: {
    source_file: ($) => seq(repeat($._line), optional($._statement)),

    _line: ($) => seq(optional($._statement), $._terminator),

    _terminator: () => /\r?\n/,

    _statement: ($) => seq(
      repeat(choice($.attribute_block, $.modifier)),
      choice(
      $.attribute_block,
      $.imports_statement,
      $.preprocessor_directive,
      $.namespace_block,
      $.class_block,
      $.module_block,
      $.structure_block,
      $.interface_block,
      $.enum_block,
      $.delegate_declaration,
      $.method_declaration,
      $.constructor_declaration,
      $.property_declaration,
      $.event_declaration,
      $.enum_member,
      $.if_statement,
      $.select_case_statement,
      $.try_statement,
      $.for_statement,
      $.for_each_statement,
      $.while_statement,
      $.do_statement,
      $.using_statement,
      $.sync_lock_statement,
      $.with_statement,
      $.return_statement,
      $.variable_declarator,
      $.field_declaration,
      $.expression_statement
      )
    ),

    imports_statement: ($) => seq($._kw_imports, field('name', $.namespace_name)),

    namespace_block: ($) => seq(
      $._kw_namespace,
      field('name', $.namespace_name),
      $._terminator,
      repeat($._line),
      $._kw_end,
      $._kw_namespace
    ),

    class_block: ($) => seq(
      $._kw_class,
      field('name', $.identifier),
      optional($.type_parameters),
      $._terminator,
      repeat($._line),
      $._kw_end,
      $._kw_class
    ),

    module_block: ($) => seq(
      $._kw_module,
      field('name', $.identifier),
      $._terminator,
      repeat($._line),
      $._kw_end,
      $._kw_module
    ),

    structure_block: ($) => seq(
      $._kw_structure,
      field('name', $.identifier),
      $._terminator,
      repeat($._line),
      $._kw_end,
      $._kw_structure
    ),

    interface_block: ($) => seq(
      $._kw_interface,
      field('name', $.identifier),
      $._terminator,
      repeat($._line),
      $._kw_end,
      $._kw_interface
    ),

    enum_block: ($) => seq(
      $._kw_enum,
      field('name', $.identifier),
      $._terminator,
      repeat($._line),
      $._kw_end,
      $._kw_enum
    ),

    delegate_declaration: ($) => seq(
      $._kw_delegate,
      choice($._kw_function, $._kw_sub),
      field('name', $.identifier),
      optional($.parameter_list),
      optional($.as_clause)
    ),

    method_declaration: ($) => prec(2, seq(
      choice($._kw_function, $._kw_sub),
      field('name', $.identifier),
      optional($.type_parameters),
      optional($.parameter_list),
      optional($.as_clause),
      $._terminator,
      repeat($._line),
      $._kw_end,
      choice($._kw_function, $._kw_sub)
    )),

    constructor_declaration: ($) => seq(
      $._kw_sub,
      $._kw_new,
      optional($.parameter_list),
      $._terminator,
      repeat($._line),
      $._kw_end,
      $._kw_sub
    ),

    property_declaration: ($) => seq(
      $._kw_property,
      field('name', $.identifier),
      optional($.as_clause)
    ),

    event_declaration: ($) => seq(
      $._kw_event,
      field('name', $.identifier),
      optional($.as_clause)
    ),

    enum_member: ($) => prec(2, seq(
      field('name', $.identifier),
      optional(seq('=', $._expression))
    )),

    if_statement: ($) => seq(
      $._kw_if,
      optional($.unknown_tail),
      $._terminator,
      repeat($._line),
      optional(seq($._kw_else, $._terminator, repeat($._line))),
      $._kw_end,
      $._kw_if
    ),

    select_case_statement: ($) => seq(
      $._kw_select,
      optional($.unknown_tail),
      $._terminator,
      repeat($._line),
      $._kw_end,
      $._kw_select
    ),

    try_statement: ($) => seq(
      $._kw_try,
      $._terminator,
      repeat($._line),
      optional(seq(choice($._kw_catch, $._kw_finally), optional($.unknown_tail), $._terminator, repeat($._line))),
      $._kw_end,
      $._kw_try
    ),

    for_statement: ($) => seq(
      $._kw_for,
      optional($.unknown_tail),
      $._terminator,
      repeat($._line),
      $._kw_next,
      optional($.unknown_tail)
    ),

    for_each_statement: ($) => seq(
      $._kw_for,
      $._kw_each,
      optional($.unknown_tail),
      $._terminator,
      repeat($._line),
      $._kw_next,
      optional($.unknown_tail)
    ),

    while_statement: ($) => seq(
      $._kw_while,
      optional($.unknown_tail),
      $._terminator,
      repeat($._line),
      $._kw_end,
      $._kw_while
    ),

    do_statement: ($) => seq(
      $._kw_do,
      optional($.unknown_tail),
      $._terminator,
      repeat($._line),
      $._kw_loop,
      optional($.unknown_tail)
    ),

    using_statement: ($) => seq(
      $._kw_using,
      optional($.unknown_tail),
      $._terminator,
      repeat($._line),
      $._kw_end,
      $._kw_using
    ),

    sync_lock_statement: ($) => seq(
      $._kw_sync_lock,
      optional($.unknown_tail),
      $._terminator,
      repeat($._line),
      $._kw_end,
      $._kw_sync_lock
    ),

    with_statement: ($) => seq(
      $._kw_with,
      optional($.unknown_tail),
      $._terminator,
      repeat($._line),
      $._kw_end,
      $._kw_with
    ),

    return_statement: ($) => prec.right(seq($._kw_return, optional($._expression))),

    variable_declarator: ($) => seq(
      $._kw_dim,
      field('name', $.identifier),
      optional($.as_clause),
      optional(seq('=', $._expression))
    ),

    field_declaration: ($) => seq(
      field('name', $.identifier),
      $.as_clause,
      optional(seq('=', $._expression))
    ),

    expression_statement: ($) => seq($._expression, optional($.unknown_tail)),

    attribute_block: ($) => seq('<', commaSep($.attribute), '>'),
    attribute: ($) => prec(1, field('name', choice($.namespace_name, $.identifier))),

    parameter_list: ($) => prec(1, seq('(', commaSep($.parameter), ')')),
    parameter: ($) => prec(1, seq(field('name', $.identifier), optional($.as_clause))),
    argument_list: ($) => seq('(', commaSep($._expression), ')'),
    type_parameters: ($) => seq($._kw_of, '(', commaSep($.type_parameter), ')'),
    type_parameter: ($) => seq(field('name', $.identifier), optional(seq($._kw_as, $.type_name))),
    type_argument_list: ($) => seq($._kw_of, '(', commaSep($.type_name), ')'),
    as_clause: ($) => seq($._kw_as, $.type_name),

    type_name: ($) => prec(1, seq(
      choice($.primitive_type, $.namespace_name, $.identifier),
      optional($.type_argument_list)
    )),

    namespace_name: ($) => seq($.identifier, repeat(seq('.', $.identifier))),

    _expression: ($) => choice(
      $.lambda_expression,
      $.invocation,
      $.member_access,
      $.parenthesized_expression,
      $.array_literal,
      $.object_initializer,
      $.interpolated_string_literal,
      $.string_literal,
      $.character_literal,
      $.date_literal,
      $.floating_point_literal,
      $.integer_literal,
      $.boolean_literal,
      $.identifier
    ),

    lambda_expression: ($) => prec.right(seq(
      $._kw_function,
      optional($.parameter_list),
      optional($._expression)
    )),

    invocation: ($) => prec(PREC.call, seq(
      field('target', choice($.member_access, $.identifier)),
      $.argument_list
    )),

    member_access: ($) => prec.left(PREC.member, seq(
      choice($.invocation, $.identifier, $.string_literal),
      '.',
      field('member', $.identifier)
    )),

    parenthesized_expression: ($) => seq('(', optional($._expression), ')'),
    array_literal: ($) => seq('{', commaSep($._expression), '}'),
    object_initializer: ($) => seq(
      $._kw_new,
      optional($.type_name),
      $._kw_with,
      $.array_literal
    ),

    primitive_type: () => choice(
      keyword('Boolean'),
      keyword('Byte'),
      keyword('Char'),
      keyword('Date'),
      keyword('Decimal'),
      keyword('Double'),
      keyword('Integer'),
      keyword('Long'),
      keyword('Object'),
      keyword('Short'),
      keyword('Single'),
      keyword('String')
    ),

    modifier: () => choice(
      keyword('Public'),
      keyword('Private'),
      keyword('Protected'),
      keyword('Friend'),
      keyword('Partial'),
      keyword('Shared'),
      keyword('ReadOnly'),
      keyword('WriteOnly'),
      keyword('Overloads'),
      keyword('Overrides'),
      keyword('MustOverride'),
      keyword('NotOverridable'),
      keyword('Async')
    ),

    boolean_literal: () => choice(keyword('True'), keyword('False')),
    integer_literal: () => token(/\d+/),
    floating_point_literal: () => token(/\d+\.\d+/),
    string_literal: () => token(/"([^"\r\n]|"")*"/),
    interpolated_string_literal: () => token(/\$"([^"\r\n]|"")*"/),
    character_literal: () => token(/"([^"\r\n]|"")*"c/),
    date_literal: () => token(/#[^#\r\n]+#/),

    comment: () => token(/'[^\r\n]*/),
    preprocessor_directive: () => token(/#[^\r\n]*/),
    unknown_tail: () => token(/[^\r\n]+/),
    unknown_statement: () => token(prec(-1, /[^\r\n]+/)),

    identifier: () => /[A-Za-z_][A-Za-z0-9_]*/,

    _kw_imports: () => keyword('Imports'),
    _kw_namespace: () => keyword('Namespace'),
    _kw_class: () => keyword('Class'),
    _kw_module: () => keyword('Module'),
    _kw_structure: () => keyword('Structure'),
    _kw_interface: () => keyword('Interface'),
    _kw_enum: () => keyword('Enum'),
    _kw_delegate: () => keyword('Delegate'),
    _kw_function: () => keyword('Function'),
    _kw_sub: () => keyword('Sub'),
    _kw_property: () => keyword('Property'),
    _kw_event: () => keyword('Event'),
    _kw_if: () => keyword('If'),
    _kw_else: () => keyword('Else'),
    _kw_end: () => keyword('End'),
    _kw_select: () => keyword('Select'),
    _kw_try: () => keyword('Try'),
    _kw_catch: () => keyword('Catch'),
    _kw_finally: () => keyword('Finally'),
    _kw_for: () => keyword('For'),
    _kw_each: () => keyword('Each'),
    _kw_next: () => keyword('Next'),
    _kw_while: () => keyword('While'),
    _kw_do: () => keyword('Do'),
    _kw_loop: () => keyword('Loop'),
    _kw_using: () => keyword('Using'),
    _kw_sync_lock: () => seq(keyword('Sync'), keyword('Lock')),
    _kw_return: () => keyword('Return'),
    _kw_dim: () => keyword('Dim'),
    _kw_private: () => keyword('Private'),
    _kw_public: () => keyword('Public'),
    _kw_friend: () => keyword('Friend'),
    _kw_protected: () => keyword('Protected'),
    _kw_as: () => keyword('As'),
    _kw_of: () => keyword('Of'),
    _kw_new: () => keyword('New'),
    _kw_with: () => keyword('With'),
  }
});
