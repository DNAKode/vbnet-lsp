(comment) @comment
(preprocessor_directive) @keyword.directive

(string_literal) @string
(interpolated_string_literal) @string
(character_literal) @string.special
(date_literal) @string.special

(integer_literal) @number
(floating_point_literal) @number
(boolean_literal) @boolean

(primitive_type) @type.builtin
(namespace_name) @namespace
(type_parameter name: (identifier) @type)

(attribute name: (_) @attribute)
(modifier) @keyword.modifier

(class_block name: (identifier) @type)
(module_block name: (identifier) @type)
(structure_block name: (identifier) @type)
(interface_block name: (identifier) @type)
(enum_block name: (identifier) @type)
(delegate_declaration name: (identifier) @type)

(method_declaration name: (identifier) @function)
(constructor_declaration) @constructor
(property_declaration name: (identifier) @property)
(event_declaration name: (identifier) @property)
(enum_member name: (identifier) @constant)
(variable_declarator name: (identifier) @variable)
(parameter name: (identifier) @variable.parameter)

(invocation target: (identifier) @function.call)
(member_access member: (identifier) @property)
