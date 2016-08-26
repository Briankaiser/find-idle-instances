#!/bin/bash

keys="$(grep -A2 'hudl_farm' ~/.aws/credentials | sed -n -e 's/aws_access_key_id=\([A-Z]*\)/--accesskey \1/p' -e 's/aws_secret_access_key=\([A-Z0-9a-z]*\)/ --secretkey \1/p' | sed '/\n$/!N;s/\n//')"

command="dotnet run"
case "$1" in 
find)
	command+=" find"
	;;
reboot)
	command+=" reboot"
	;;
*)
	echo $"Usage $0 {find|reboot} {stream|stream-med|stream-lrg} {use1|apse2|euw1}"
	exit 1
esac

command+=" $keys --securityGroup prod-streamworker"


case "$2" in
stream-lrg)
	command+="-lrg --name prod-farm-stream-lrg"
	;;
stream-med)
	command+="-med --name prod-farm-stream-med"
	;;
stream)
	command+=" --name prod-farm-stream"
	;;
*)
	echo $"Usage $0 {find|reboot} {stream|stream-med|stream-lrg} {use1|apse2|euw1}"
	exit 1
esac

case "$3" in
apse2)
	command+="-apse2 --region ap-southeast-2"
	;;
euw1)
	command+="-euw1 --region eu-west-1"
	;;
use1)
	;;
*)
	echo $"Usage $0 {find|reboot} {stream|stream-med|stream-lrg} {use1|apse2|euw1}"
	exit 1
esac
echo "$command"
exit 0
