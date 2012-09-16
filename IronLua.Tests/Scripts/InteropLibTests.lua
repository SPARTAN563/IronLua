﻿print("Testing IronLua's CLR implementation")

system = {}

system.convert = clr.import('System.Convert')
assert(system.convert,'Failed to import System.Convert')

system.int = clr.import('System.Int32')
assert(system.int,'Failed to import System.Int32')

system.short = clr.import('System.Int16')
assert(system.short,'Failed to import System.Int16')

print("Attempting: Convert.ToInt32(12)")
num1 = system.convert.ToInt32(12)
assert(tostring(num1) == '12',"Failed to convert double to Int32")
print "    Success"

print("Attempting to create an array: Int32[10]")
array = clr.makearray(system.int, 10)
assert(array,'Failed to create array')
assert(#array == 10,'Array length was not correct')
print('    Success')
print('Testing array access')
array[0]=10
print("array[0] = ",array[0])
assert(array[0] == system.convert.ToInt32(10),'Array value was not set correctly')
assert(array[0] == 10,'Implicit conversion between Int32 and double failed')
print('    Success')

print('Testing Array Itteration')
for i=1,#array - 1 do
	array[i] = 10 - i
end

for v in clr.itterate(array) do
	if not itterated then 
		itterated = v
	else
		itterated = itterated..','..v
	end
end
assert(itterated == '10,9,8,7,6,5,4,3,2,1', 'Array itteration failed, got '..itterated)
print('    Success')

print('Attempting to import List<string>')
system.string = clr.import('System.String')
assert(system.string,'Failed to import System.String')
system.list_string = clr.import('System.Collections.Generic.List`1[System.String]')
assert(system.list_string, 'Failed to import System.Collections.Generic.List<string>')
print('    Success')

print([[Testing List<string> access]])
list = system.list_string()
assert(list,'Failed to instantiate List<string>')
list.Add('This is a test string')
assert(list[0] == 'This is a test string','Failed to retreive value from list')
print('    Success')

print"Testing List<string> itteration"
for i=1,10 do list.Add("This is a test string") end

for v in clr.itterate(list) do assert(v == 'This is a test string', 'List value was incorrect') end
print('    Success')